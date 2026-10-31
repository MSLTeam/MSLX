using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.Daemon.Services.DeployServerService;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files/upload")]
[Authorize]
public class UploadController : ControllerBase
{
    private static readonly uint[] Crc32cTable = CreateCrc32cTable();

    private readonly string _tempPath;
    private readonly IMemoryCache _memoryCache;
    private readonly UploadSessionService _uploadSessions;
    private readonly ServerDeploymentService _deploymentService;

    public UploadController(IMemoryCache memoryCache, UploadSessionService uploadSessions, ServerDeploymentService deploymentService)
    {
        _memoryCache = memoryCache;
        _uploadSessions = uploadSessions;
        _deploymentService = deploymentService;
        // 临时文件存放目录
        _tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
        if (!Directory.Exists(_tempPath)) Directory.CreateDirectory(_tempPath);
    }

    /// <summary>
    /// 获取上传key
    /// </summary>
    [HttpPost("init")]
    public IActionResult InitUpload(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FileUploadInitRequest? request = null)
    {
        var resumeKey = BuildResumeSessionKey(request);
        if (resumeKey != null && _memoryCache.TryGetValue(resumeKey, out string? existingUploadId) &&
            !string.IsNullOrWhiteSpace(existingUploadId) &&
            _uploadSessions.IsOwned(existingUploadId, User) &&
            !System.IO.File.Exists(Path.Combine(_tempPath, existingUploadId + ".tmp")))
        {
            var receivedChunks = GetChunkIndices(existingUploadId);
            return Ok(new ApiResponse<JObject>
            {
                Code = 200,
                Message = "已找到可恢复的上传任务",
                Data = new JObject
                {
                    ["uploadId"] = existingUploadId,
                    ["resumed"] = true,
                    ["receivedChunks"] = new JArray(receivedChunks),
                },
            });
        }

        var uploadId = Guid.NewGuid().ToString("N");

        // 2h 有效；Daemon 重启时由 TempFileCleanupService 清理临时文件，会话也不会恢复。
        _uploadSessions.Create(uploadId, User, request?.NodeId);
        if (resumeKey != null)
        {
            _memoryCache.Set(resumeKey, uploadId, TimeSpan.FromHours(2));
        }

        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "获取成功",
            Data = new JObject()
            {
                ["uploadId"] = uploadId,
                ["resumed"] = false,
                ["receivedChunks"] = new JArray(),
            }
        });
    }

    private string? BuildResumeSessionKey(FileUploadInitRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FileName) ||
            string.IsNullOrWhiteSpace(request.Fingerprint) || request.FileSize is null or < 0 ||
            request.LastModified is null or < 0 || request.ChunkSize is null or <= 0 ||
            request.TotalChunks is null or <= 0)
        {
            return null;
        }

        var userId = _uploadSessions.ResolveOwnerId(User);
        if (string.IsNullOrWhiteSpace(userId)) return null;
        var descriptor = string.Join("|", userId, request.NodeId ?? "local", request.FileName,
            request.FileSize.Value, request.LastModified.Value, request.ChunkSize.Value,
            request.TotalChunks.Value, request.Fingerprint);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(descriptor)));
        return $"Upload_Resume_{digest}";
    }

    /// <summary>
    /// 上传分片
    /// </summary>
    [HttpPost("chunk/{uploadId}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadChunk(
        string uploadId,
        [FromForm] int index,
        [FromForm] IFormFile file,
        [FromForm] string? crc32c = null)
    {
        // 检查凭证
        if (!_uploadSessions.IsOwned(uploadId, User))
        {
            return BadRequest(new ApiResponse<string> { Code = 403, Message = "上传凭证无效或已过期，请刷新页面重试" });
        }

        // 校验
        if (!Regex.IsMatch(uploadId, "^[a-fA-F0-9]{32}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "非法 ID" });
        }

        if (index < 0 || index > 1_000_000)
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "分片索引超出范围" });
        }

        if (file == null || file.Length == 0) return BadRequest("文件为空");
        if (!string.IsNullOrWhiteSpace(crc32c) && !Regex.IsMatch(crc32c, "^[a-fA-F0-9]{8}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "分片 CRC32C 格式无效" });
        }

        // 暂存为 uploadId_index
        var chunkPath = Path.Combine(_tempPath, $"{uploadId}_{index}");
        var tempChunkPath = chunkPath + ".uploading";

        uint crc = 0xffffffff;
        long bytesWritten = 0;
        var buffer = new byte[1024 * 1024];
        await using (var input = file.OpenReadStream())
        await using (var stream = new FileStream(tempChunkPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read));
                crc = UpdateCrc32c(crc, buffer.AsSpan(0, read));
                bytesWritten += read;
            }
        }

        crc ^= 0xffffffff;
        var actualCrc32c = crc.ToString("x8");
        if (!string.IsNullOrWhiteSpace(crc32c) && !string.Equals(crc32c, actualCrc32c, StringComparison.OrdinalIgnoreCase))
        {
            System.IO.File.Delete(tempChunkPath);
            return UnprocessableEntity(new ApiResponse<JObject>
            {
                Code = 422,
                Message = "分片完整性校验失败，请重试",
                Data = new JObject
                {
                    ["index"] = index,
                    ["expectedCrc32c"] = crc32c,
                    ["actualCrc32c"] = actualCrc32c,
                },
            });
        }

        // CRC 元数据与分片一起落盘，合并时可以再次验证磁盘上的分片内容。
        var crcMetadataPath = chunkPath + ".crc32c";
        var tempCrcMetadataPath = crcMetadataPath + ".uploading";
        await System.IO.File.WriteAllTextAsync(tempCrcMetadataPath, actualCrc32c);
        System.IO.File.Move(tempCrcMetadataPath, crcMetadataPath, true);

        // 使用原子替换，避免状态接口把半写入文件当成已完成分片。
        System.IO.File.Move(tempChunkPath, chunkPath, true);

        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "上传完成",
            Data = new JObject
            {
                ["index"] = index,
                ["size"] = bytesWritten,
                ["crc32c"] = actualCrc32c,
            },
        });
    }

    /// <summary>
    /// 查询已接收的分片，用于网络中断后的恢复。
    /// </summary>
    [HttpGet("status/{uploadId}")]
    public IActionResult UploadStatus(string uploadId)
    {
        if (!Regex.IsMatch(uploadId ?? string.Empty, "^[a-fA-F0-9]{32}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "非法 ID" });
        }

        if (!_uploadSessions.IsOwned(uploadId, User))
        {
            return BadRequest(new ApiResponse<string> { Code = 403, Message = "上传凭证无效或已过期，请刷新页面重试" });
        }

        var receivedChunks = GetChunkIndices(uploadId);
        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "查询成功",
            Data = new JObject
            {
                ["uploadId"] = uploadId,
                ["receivedChunks"] = new JArray(receivedChunks),
                ["receivedCount"] = receivedChunks.Count,
                ["status"] = "uploading",
            },
        });
    }

    /// <summary>
    /// 合并分片
    /// </summary>
    [HttpPost("finish/{uploadId}")]
    public async Task<IActionResult> MergeChunks(string uploadId, [FromBody] FileUploadMergeRequest req)
    {
        // 验证key
        if (!_uploadSessions.IsOwned(uploadId, User))
        {
            return BadRequest(new ApiResponse<string> { Code = 403, Message = "凭证无效或已过期" });
        }

        if (req == null || req.TotalChunks <= 0)
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "分片总数必须大于 0" });
        }

        if (req.ChunkSize is <= 0 || req.TotalSize is < 0 ||
            (!string.IsNullOrWhiteSpace(req.Sha256) && !Regex.IsMatch(req.Sha256, "^[a-fA-F0-9]{64}$")))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "分片完整性参数无效" });
        }

        if (req.ChunkSize.HasValue && req.TotalSize.HasValue)
        {
            var expectedTotalChunks = (req.TotalSize.Value + req.ChunkSize.Value - 1) / req.ChunkSize.Value;
            if (expectedTotalChunks != req.TotalChunks)
            {
                return BadRequest(new ApiResponse<string>
                {
                    Code = 400,
                    Message = $"分片总数不匹配，按文件大小应为 {expectedTotalChunks}，实际为 {req.TotalChunks}",
                });
            }
        }

        var finalPath = Path.Combine(_tempPath, uploadId + ".tmp");

        // 响应在网络中丢失时，客户端会重试 finish；已合并文件可以安全地重复确认。
        if (System.IO.File.Exists(finalPath))
        {
            var existingSize = new FileInfo(finalPath).Length;
            if (req.TotalSize.HasValue && existingSize != req.TotalSize.Value)
            {
                return BadRequest(new ApiResponse<JObject>
                {
                    Code = 400,
                    Message = $"已合并文件大小不匹配，期望 {req.TotalSize.Value}，实际 {existingSize}",
                });
            }

            var existingSha256 = await ComputeFileSha256Async(finalPath);
            if (!string.IsNullOrWhiteSpace(req.Sha256) &&
                !string.Equals(existingSha256, req.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new ApiResponse<JObject>
                {
                    Code = 422,
                    Message = "已合并文件 SHA-256 与源文件不匹配",
                    Data = new JObject
                    {
                        ["expectedSha256"] = req.Sha256,
                        ["actualSha256"] = existingSha256,
                    },
                });
            }

            return Ok(new ApiResponse<JObject>
            {
                Code = 200,
                Message = "文件上传全部成功",
                Data = new JObject
                {
                    ["uploadId"] = uploadId,
                    ["size"] = existingSize,
                    ["sha256"] = existingSha256,
                },
            });
        }

        // 获取所有分片文件
        var chunksWithIndex = GetChunkFiles(uploadId);
        var orderedChunks = chunksWithIndex.OrderBy(item => item.Index).ToList();
        var chunks = orderedChunks.Select(item => item.Path).ToList();

        var receivedIndices = chunksWithIndex.Select(item => item.Index).ToHashSet();
        var missingIndices = Enumerable.Range(0, req.TotalChunks).Where(index => !receivedIndices.Contains(index)).ToList();
        if (chunks.Count != req.TotalChunks || receivedIndices.Any(index => index < 0 || index >= req.TotalChunks))
        {
            return BadRequest(new ApiResponse<JObject>
            {
                Code = 400,
                Message = $"分片集合不完整，期望 {req.TotalChunks} 个，实际找到 {chunks.Count} 个",
                Data = new JObject
                {
                    ["receivedChunks"] = new JArray(receivedIndices.OrderBy(index => index)),
                    ["missingChunks"] = new JArray(missingIndices),
                },
            });
        }

        var mergingPath = finalPath + ".merging";
        byte[] finalHash;
        long mergedSize = 0;
        try
        {
            await using (var destStream = new FileStream(mergingPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1024 * 1024];
                foreach (var chunk in orderedChunks)
                {
                    var chunkFile = chunk.Path;
                    if (req.ChunkSize.HasValue && req.TotalSize.HasValue)
                    {
                        var expectedLength = chunk.Index == req.TotalChunks - 1
                            ? req.TotalSize.Value - ((long)req.ChunkSize.Value * (req.TotalChunks - 1))
                            : req.ChunkSize.Value;
                        var actualLength = new FileInfo(chunkFile).Length;
                        if (expectedLength <= 0 || actualLength != expectedLength)
                        {
                            System.IO.File.Delete(mergingPath);
                            return UnprocessableEntity(new ApiResponse<JObject>
                            {
                                Code = 422,
                                Message = $"分片 {chunk.Index} 大小不匹配，期望 {expectedLength}，实际 {actualLength}",
                                Data = new JObject
                                {
                                    ["index"] = chunk.Index,
                                    ["invalidChunks"] = new JArray(chunk.Index),
                                    ["expectedSize"] = expectedLength,
                                    ["actualSize"] = actualLength,
                                },
                            });
                        }
                    }

                    string? expectedChunkCrc32c;
                    try
                    {
                        expectedChunkCrc32c = await ReadChunkCrc32cAsync(chunkFile);
                    }
                    catch (InvalidDataException)
                    {
                        System.IO.File.Delete(mergingPath);
                        return UnprocessableEntity(new ApiResponse<JObject>
                        {
                            Code = 422,
                            Message = $"分片 {chunk.Index} 的完整性元数据无效",
                            Data = new JObject
                            {
                                ["index"] = chunk.Index,
                                ["invalidChunks"] = new JArray(chunk.Index),
                            },
                        });
                    }

                    if (string.IsNullOrWhiteSpace(expectedChunkCrc32c) && !string.IsNullOrWhiteSpace(req.Sha256))
                    {
                        System.IO.File.Delete(mergingPath);
                        return UnprocessableEntity(new ApiResponse<JObject>
                        {
                            Code = 422,
                            Message = $"分片 {chunk.Index} 缺少完整性元数据",
                            Data = new JObject
                            {
                                ["index"] = chunk.Index,
                                ["invalidChunks"] = new JArray(chunk.Index),
                            },
                        });
                    }
                    uint chunkCrc = 0xffffffff;
                    await using var sourceStream = new FileStream(chunkFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                    int read;
                    while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                    {
                        await destStream.WriteAsync(buffer.AsMemory(0, read));
                        chunkCrc = UpdateCrc32c(chunkCrc, buffer.AsSpan(0, read));
                        sha256.AppendData(buffer.AsSpan(0, read));
                        mergedSize += read;
                    }

                    if (expectedChunkCrc32c != null)
                    {
                        var actualChunkCrc32c = (chunkCrc ^ 0xffffffff).ToString("x8");
                        if (!string.Equals(expectedChunkCrc32c, actualChunkCrc32c, StringComparison.OrdinalIgnoreCase))
                        {
                            System.IO.File.Delete(mergingPath);
                            return UnprocessableEntity(new ApiResponse<JObject>
                            {
                                Code = 422,
                                Message = $"分片 {Path.GetFileName(chunkFile)} 完整性校验失败",
                                Data = new JObject
                                {
                                    ["index"] = chunk.Index,
                                    ["invalidChunks"] = new JArray(chunk.Index),
                                },
                            });
                        }
                    }
                }

                finalHash = sha256.GetHashAndReset();
            }

            if (req.TotalSize.HasValue && mergedSize != req.TotalSize.Value)
            {
                System.IO.File.Delete(mergingPath);
                return BadRequest(new ApiResponse<JObject>
                {
                    Code = 400,
                    Message = $"合并文件大小不匹配，期望 {req.TotalSize.Value}，实际 {mergedSize}",
                });
            }

            var actualSha256 = Convert.ToHexString(finalHash).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(req.Sha256) &&
                !string.Equals(actualSha256, req.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.Delete(mergingPath);
                return UnprocessableEntity(new ApiResponse<JObject>
                {
                    Code = 422,
                    Message = "合并文件 SHA-256 与源文件不匹配",
                    Data = new JObject
                    {
                        ["expectedSha256"] = req.Sha256,
                        ["actualSha256"] = actualSha256,
                    },
                });
            }

            // 只有完整性校验通过后才替换最终文件，避免留下半成品。
            System.IO.File.Move(mergingPath, finalPath, true);
        }
        catch (Exception ex)
        {
            try { System.IO.File.Delete(mergingPath); } catch { }
            return StatusCode(500, new ApiResponse<JObject>
            {
                Code = 500,
                Message = $"合并失败: {ex.Message}"
            });
        }
        // 合并成功后再清理分片；合并失败时保留它们，客户端可以查询状态后继续。
        // 清理分片
        foreach (var chunk in chunks)
        {
            try
            {
                System.IO.File.Delete(chunk);
            }
            catch
            {
            }

            try
            {
                System.IO.File.Delete(chunk + ".crc32c");
            }
            catch
            {
            }
        }

        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "文件上传全部成功",
            Data = new JObject()
            {
                ["uploadId"] = uploadId,
                ["size"] = mergedSize,
                ["sha256"] = Convert.ToHexString(finalHash).ToLowerInvariant(),
            }
        });
    }

    private static uint[] CreateCrc32cTable()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (uint)i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0x82f63b78 ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    private static uint UpdateCrc32c(uint crc, ReadOnlySpan<byte> buffer)
    {
        foreach (var value in buffer)
        {
            crc = Crc32cTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return crc;
    }

    private static async Task<string> ComputeFileSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            sha256.AppendData(buffer.AsSpan(0, read));
        }

        return Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<string?> ReadChunkCrc32cAsync(string chunkPath)
    {
        var metadataPath = chunkPath + ".crc32c";
        if (!System.IO.File.Exists(metadataPath)) return null;
        var value = (await System.IO.File.ReadAllTextAsync(metadataPath)).Trim();
        if (!Regex.IsMatch(value, "^[a-fA-F0-9]{8}$"))
        {
            throw new InvalidDataException($"分片 {Path.GetFileName(chunkPath)} 的 CRC32C 元数据无效");
        }

        return value;
    }

    private List<int> GetChunkIndices(string uploadId)
    {
        return GetChunkFiles(uploadId)
            .Select(item => item.Index)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private List<(int Index, string Path)> GetChunkFiles(string uploadId)
    {
        var prefix = uploadId + "_";
        return Directory.EnumerateFiles(_tempPath, prefix + "*", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                // 解析文件名中的 index
                var fileName = Path.GetFileName(path);
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(fileName[prefix.Length..], out var index))
                {
                    return (Index: -1, Path: path);
                }

                return (Index: index, Path: path);
            })
            .Where(item => item.Index >= 0)
            .ToList();
    }

    /// <summary>
    /// 删除临时文件
    /// </summary>
    [HttpPost("delete/{uploadId}")]
    public IActionResult DeleteUpload(string uploadId)
    {
        // 简单的格式安全校验
        if (string.IsNullOrEmpty(uploadId) || !Regex.IsMatch(uploadId, "^[a-fA-F0-9]{32}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "ID 格式错误" });
        }

        // 从缓存移除
        if (!_uploadSessions.IsOwned(uploadId, User))
        {
            return BadRequest(new ApiResponse<string> { Code = 403, Message = "上传凭证无效或已过期，请刷新页面重试" });
        }

        _uploadSessions.Remove(uploadId);

        var mergedPath = Path.Combine(_tempPath, uploadId + ".tmp");

        try
        {
            // 删除合并后的文件
            if (System.IO.File.Exists(mergedPath))
            {
                System.IO.File.Delete(mergedPath);
            }

            // 清理可能残留的分片
            var chunks = Directory.GetFiles(_tempPath, $"{uploadId}_*");
            foreach (var chunk in chunks)
            {
                try
                {
                    System.IO.File.Delete(chunk);
                }
                catch
                {
                }
            }

            var mergingPath = Path.Combine(_tempPath, uploadId + ".tmp.merging");
            if (System.IO.File.Exists(mergingPath))
            {
                System.IO.File.Delete(mergingPath);
            }

            return Ok(new ApiResponse<string> { Code = 200, Message = "清理成功" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<string> { Code = 500, Message = $"删除失败: {ex.Message}" });
        }
    }

    // 检查压缩包里面的jar文件 给整合包模式用的
    [HttpGet("inspect/{uploadId?}")]
    [Authorize(Roles = "admin")]
    public IActionResult InspectArchive(string? uploadId, [FromQuery] string? localPath = null)
    {
        string finalPath;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            // 本机路径
            finalPath = localPath;
        }
        else
        {
            // 上传的整合包
            if (string.IsNullOrEmpty(uploadId) || !Regex.IsMatch(uploadId, "^[a-fA-F0-9]{32}$"))
            {
                return BadRequest(new ApiResponse<string> { Code = 400, Message = "ID 格式错误" });
            }
            if (!_uploadSessions.IsOwned(uploadId, User))
            {
                return BadRequest(new ApiResponse<string> { Code = 403, Message = "上传凭证无效或已过期，请刷新页面重试" });
            }
            finalPath = Path.Combine(_tempPath, uploadId + ".tmp");
        }

        if (!System.IO.File.Exists(finalPath))
        {
            return NotFound(new ApiResponse<string> { Code = 404, Message = "找不到指定的压缩包文件，请检查路径或文件是否已过期" });
        }

        try
        {
            using var archive = ZipFile.Open(finalPath, ZipArchiveMode.Read, System.Text.Encoding.GetEncoding("GBK"));

            var mrpackData = _deploymentService.InspectMrpackArchive(archive);
            if (mrpackData != null)
            {
                return Ok(new ApiResponse<JObject>
                {
                    Code = 200,
                    Message = "解析成功",
                    Data = mrpackData
                });
            }
            
            // 获取所有 Entry 的顶层路径片段
            var topLevelSegments = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.FullName))
                .Select(e => e.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(s => s != null)
                .Distinct()
                .ToList();

            string basePrefix = "";

            // 如果顶层只有一个片段 那就可能是套了个文件夹
            if (topLevelSegments.Count == 1)
            {
                var potentialRoot = topLevelSegments[0];

                // 检查这是否真的是个目录
                bool isFolderWrapper = archive.Entries.Any(e =>
                    e.FullName.StartsWith(potentialRoot + "/", StringComparison.OrdinalIgnoreCase));

                if (isFolderWrapper)
                {
                    basePrefix = potentialRoot + "/";
                }
            }

            // 扫描普通的单jar文件核心
            var jarFiles = archive.Entries
                // 处理Windows下的路径符号
                .Select(e => new { 
                    Entry = e, 
                    NormalizedName = e.FullName.Replace('\\', '/') 
                })
                .Where(x =>
                        !x.NormalizedName.EndsWith("/") && // 排除目录本身
                        x.NormalizedName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) // 只要 jar
                )
                .Select(x =>
                {
                    var name = x.NormalizedName;
                    // 如果有 basePrefix 只处理在这个目录下的文件
                    if (!string.IsNullOrEmpty(basePrefix))
                    {
                        if (!name.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
                            return null; // 不在根目录下

                        // 获取相对路径
                        return name.Substring(basePrefix.Length);
                    }

                    return name;
                })
                .Where(name => !string.IsNullOrEmpty(name)) // 过滤掉不符合前缀的
                .Where(name => !name!.Contains("/")) 
                .ToList();

            // 纯 NeoForge / Forge 端扫描
            bool isWindows = PlatFormServices.GetOs() == "Windows";
            string targetArgFile = isWindows ? "win_args.txt" : "unix_args.txt";

            var loaderPaths = new[]
            {
                $"{basePrefix}libraries/net/neoforged/neoforge/",
                $"{basePrefix}libraries/net/minecraftforge/forge/"
            };

            foreach (var basePath in loaderPaths)
            {
                var versions = archive.Entries
                    .Where(e => e.FullName.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) &&
                                e.FullName.Length > basePath.Length)
                    .Select(e =>
                    {
                        var relativePath = e.FullName.Substring(basePath.Length);
                        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        return parts.Length > 0 ? parts[0] : null;
                    })
                    .Where(v => v != null)
                    .Distinct()
                    .ToList();

                foreach (var version in versions)
                {
                    string versionPrefix = $"{basePath}{version}/";

                    // 检查该版本目录下是否至少有一个 jar 文件，用以判断安装情况
                    bool hasJar = archive.Entries.Any(e =>
                        e.FullName.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                        !e.FullName.Substring(versionPrefix.Length).Contains("/")
                    );

                    if (hasJar)
                    {
                        var argEntry = archive.Entries.FirstOrDefault(e =>
                            e.FullName.Equals($"{versionPrefix}{targetArgFile}", StringComparison.OrdinalIgnoreCase));

                        if (argEntry != null)
                        {
                            // 获取相对路径
                            string relativePath = string.IsNullOrEmpty(basePrefix)
                                ? argEntry.FullName
                                : argEntry.FullName.Substring(basePrefix.Length);
                            
                            jarFiles.Add($"@{relativePath}");
                        }
                    }
                }
            }

            // 扫描整合包根目录中的启动脚本
            var scriptPriority = new[] { "start", "run", "launch", "server" };

            var scripts = archive.Entries
                .Select(e => new { NormalizedName = e.FullName.Replace('\\', '/') })
                .Where(x =>
                    !x.NormalizedName.EndsWith("/") &&
                    (string.IsNullOrEmpty(basePrefix)
                        ? !x.NormalizedName.Contains("/")
                        : x.NormalizedName.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase) &&
                          !x.NormalizedName.Substring(basePrefix.Length).Contains("/")))
                .Select(x => string.IsNullOrEmpty(basePrefix)
                    ? x.NormalizedName
                    : x.NormalizedName.Substring(basePrefix.Length))
                .Where(name =>
                    name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name =>
                {
                    var baseName = Path.GetFileNameWithoutExtension(name).ToLower();
                    var idx = Array.IndexOf(scriptPriority, baseName);
                    return idx >= 0 ? idx : scriptPriority.Length;
                })
                .ToList();

            var metadataEntry = archive.Entries.FirstOrDefault(e => 
                e.FullName.Equals($"{basePrefix}mslx-pack-metadata.json", StringComparison.OrdinalIgnoreCase));
            
            JObject metadata = null;
            if (metadataEntry != null)
            {
                using var stream = metadataEntry.Open();
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                metadata = JObject.Parse(reader.ReadToEnd());
            }

            return Ok(new ApiResponse<JObject>
            {
                Code = 200,
                Message = "解析成功",
                Data = new JObject
                {
                    ["count"] = jarFiles.Count,
                    ["jars"] = JToken.FromObject(jarFiles),
                    ["scripts"] = JToken.FromObject(scripts),
                    ["detectedRoot"] = basePrefix,
                    ["metadata"] = metadata,
                    ["format"] = "zip"
                }
            });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "文件不是有效的压缩包" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<string> { Code = 500, Message = $"读取压缩包失败: {ex.Message}" });
        }
    }
}
