using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Controllers.FilesControllers;
using MSLX.Daemon.Services;
using MSLX.Daemon.Services.DeployServerService;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models.Files;

namespace MSLX.Tests;

public sealed class UploadControllerTests
{
    [Fact]
    public async Task ConcurrentFinishRequestsAreIdempotent()
    {
        const string uploadId = "0123456789abcdef0123456789abcde1";
        const int chunkSize = 512 * 1024;
        const int totalChunks = 20;
        var tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
        Directory.CreateDirectory(tempPath);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new UploadSessionService(cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("UserId", "upload-test-user") }, "test"));
        sessions.Create(uploadId, principal, "local");

        var controller = new UploadController(
            cache,
            sessions,
            new ServerDeploymentService(NullLogger<ServerDeploymentService>.Instance, null!))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };

        var chunk = new byte[chunkSize];
        new Random(42).NextBytes(chunk);
        try
        {
            for (var index = 0; index < totalChunks; index++)
            {
                var chunkPath = Path.Combine(tempPath, $"{uploadId}_{index}");
                await File.WriteAllBytesAsync(chunkPath, chunk);
                await File.WriteAllTextAsync(chunkPath + ".crc32c", ComputeCrc32c(chunk));
            }

            var request = new FileUploadMergeRequest
            {
                TotalChunks = totalChunks,
                ChunkSize = chunkSize,
                TotalSize = (long)chunkSize * totalChunks,
            };

            var results = await Task.WhenAll(
                controller.MergeChunks(uploadId, request),
                controller.MergeChunks(uploadId, request));

            Assert.All(results, result => Assert.IsType<OkObjectResult>(result));
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(tempPath, $"{uploadId}*"))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    [Fact]
    public async Task UploadAndMergeUseTheSameSessionLock()
    {
        const string uploadId = "0123456789abcdef0123456789abcde2";
        var tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
        Directory.CreateDirectory(tempPath);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new UploadSessionService(cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("UserId", "upload-lock-test-user") }, "test"));
        sessions.Create(uploadId, principal, "local");

        var controller = new UploadController(
            cache,
            sessions,
            new ServerDeploymentService(NullLogger<ServerDeploymentService>.Instance, null!))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };

        var payload = new byte[1024 * 1024];
        new Random(43).NextBytes(payload);
        await using var source = new BlockingReadStream(payload);
        var formFile = new BlockingFormFile(source);

        try
        {
            var uploadTask = controller.UploadChunk(uploadId, 0, formFile);
            await source.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));

            var mergeTask = controller.MergeChunks(uploadId, new FileUploadMergeRequest
            {
                TotalChunks = 1,
                ChunkSize = payload.Length,
                TotalSize = payload.Length,
            });

            await Assert.ThrowsAsync<TimeoutException>(() => mergeTask.WaitAsync(TimeSpan.FromMilliseconds(100)));

            source.ReleaseRead();
            Assert.IsType<OkObjectResult>(await uploadTask);
            Assert.IsType<OkObjectResult>(await mergeTask);
            Assert.True(File.Exists(Path.Combine(tempPath, uploadId + ".tmp")));
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(tempPath, $"{uploadId}*"))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    [Fact]
    public async Task MergeRejectsInvalidChunkBeforeCreatingMergedFile()
    {
        const string uploadId = "0123456789abcdef0123456789abcde3";
        const int chunkSize = 1024;
        const int totalChunks = 2;
        var tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
        Directory.CreateDirectory(tempPath);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new UploadSessionService(cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("UserId", "upload-validation-test-user") }, "test"));
        sessions.Create(uploadId, principal, "local");

        var controller = new UploadController(
            cache,
            sessions,
            new ServerDeploymentService(NullLogger<ServerDeploymentService>.Instance, null!))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };

        try
        {
            var validChunk = new byte[chunkSize];
            await using var validStream = new MemoryStream(validChunk);
            var validFile = new FormFile(validStream, 0, validChunk.Length, "file", "chunk0.bin");
            Assert.IsType<OkObjectResult>(await controller.UploadChunk(
                uploadId, 0, validFile, ComputeCrc32c(validChunk)));

            var invalidChunk = Enumerable.Repeat((byte)1, chunkSize).ToArray();
            await using var invalidStream = new MemoryStream(invalidChunk);
            var invalidFile = new FormFile(invalidStream, 0, invalidChunk.Length, "file", "chunk1.bin");
            Assert.IsType<OkObjectResult>(await controller.UploadChunk(
                uploadId, 1, invalidFile, "deadbeef"));

            var result = await controller.MergeChunks(uploadId, new FileUploadMergeRequest
            {
                TotalChunks = totalChunks,
                ChunkSize = chunkSize,
                TotalSize = (long)chunkSize * totalChunks,
            });

            Assert.IsType<UnprocessableEntityObjectResult>(result);
            Assert.False(File.Exists(Path.Combine(tempPath, uploadId + ".tmp")));
            Assert.False(File.Exists(Path.Combine(tempPath, uploadId + ".tmp.merging")));
            Assert.True(File.Exists(Path.Combine(tempPath, $"{uploadId}_0")));
            Assert.True(File.Exists(Path.Combine(tempPath, $"{uploadId}_1")));
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(tempPath, $"{uploadId}*"))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    [Fact]
    public async Task ValidatedChunkCannotBeOverwrittenByRetry()
    {
        const string uploadId = "0123456789abcdef0123456789abcde4";
        var tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
        Directory.CreateDirectory(tempPath);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new UploadSessionService(cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("UserId", "upload-validation-lock-test-user") }, "test"));
        sessions.Create(uploadId, principal, "local");

        var controller = new UploadController(
            cache,
            sessions,
            new ServerDeploymentService(NullLogger<ServerDeploymentService>.Instance, null!))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };

        var original = Enumerable.Repeat((byte)7, 1024).ToArray();
        var replacement = Enumerable.Repeat((byte)8, 1024).ToArray();
        var chunkPath = Path.Combine(tempPath, $"{uploadId}_0");

        try
        {
            await using var originalStream = new MemoryStream(original);
            var originalFile = new FormFile(originalStream, 0, original.Length, "file", "chunk.bin");
            Assert.IsType<OkObjectResult>(await controller.UploadChunk(
                uploadId, 0, originalFile, ComputeCrc32c(original)));

            for (var attempt = 0; attempt < 50 && !File.Exists(chunkPath + ".crc32c"); attempt++)
            {
                await Task.Delay(20);
            }

            Assert.True(File.Exists(chunkPath + ".crc32c"));

            await using var replacementStream = new MemoryStream(replacement);
            var replacementFile = new FormFile(replacementStream, 0, replacement.Length, "file", "chunk.bin");
            Assert.IsType<OkObjectResult>(await controller.UploadChunk(uploadId, 0, replacementFile, "deadbeef"));
            Assert.Equal(original, await File.ReadAllBytesAsync(chunkPath));
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(tempPath, $"{uploadId}*"))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    private sealed class BlockingReadStream(byte[] data) : MemoryStream(data)
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hasBlocked;

        public Task ReadStarted => _readStarted.Task;

        public void ReleaseRead() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hasBlocked, 1) == 0)
            {
                _readStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class BlockingFormFile(BlockingReadStream stream) : IFormFile
    {
        public string ContentType => "application/octet-stream";
        public string ContentDisposition => "";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => stream.Length;
        public string Name => "file";
        public string FileName => "chunk.bin";
        public Stream OpenReadStream() => stream;
        public void CopyTo(Stream target) => stream.CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => stream.CopyToAsync(target, cancellationToken);
    }

    private static string ComputeCrc32c(byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (var value in data)
        {
            var current = (uint)value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mix = (crc ^ current) & 1;
                crc >>= 1;
                if (mix != 0) crc ^= 0x82f63b78;
                current >>= 1;
            }
        }

        return (crc ^ 0xffffffff).ToString("x8");
    }
}
