using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Services;
using Xunit;

namespace MSLX.Tests;

public class ArchiveServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ArchiveService _archiveService;

    public ArchiveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MSLX_ArchiveTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _archiveService = new ArchiveService(NullLogger<ArchiveService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Theory]
    [InlineData("test.zip")]
    [InlineData("test.tar")]
    [InlineData("test.tar.gz")]
    [InlineData("test.tar.bz2")]
    public async Task CompressAndDecompress_MultipleFormats_Success(string archiveFileName)
    {
        // 准备源测试文件
        string srcDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(srcDir);

        string file1 = Path.Combine(srcDir, "hello.txt");
        string file2 = Path.Combine(srcDir, "sub", "world.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file2)!);

        await File.WriteAllTextAsync(file1, "Hello MSLX Archive!", Encoding.UTF8);
        await File.WriteAllTextAsync(file2, "Sub folder content", Encoding.UTF8);

        var filesToCompress = new Dictionary<string, string>
        {
            [file1] = "hello.txt",
            [file2] = "sub/world.txt"
        };

        string archivePath = Path.Combine(_tempDir, archiveFileName);

        // 1. 测试压缩
        await _archiveService.CompressAsync(filesToCompress, archivePath, CancellationToken.None);
        Assert.True(File.Exists(archivePath), $"压缩文件应当存在: {archivePath}");
        Assert.True(new FileInfo(archivePath).Length > 0, "压缩文件大小应当大于0");

        // 2. 测试解压
        string extractDir = Path.Combine(_tempDir, "extracted_" + Path.GetFileNameWithoutExtension(archiveFileName));
        await _archiveService.DecompressAsync(archivePath, extractDir, "utf-8", CancellationToken.None);

        string extractedFile1 = Path.Combine(extractDir, "hello.txt");
        string extractedFile2 = Path.Combine(extractDir, "sub", "world.txt");

        Assert.True(File.Exists(extractedFile1), $"解压后文件应当存在: {extractedFile1}");
        Assert.True(File.Exists(extractedFile2), $"解压后子文件应当存在: {extractedFile2}");

        string content1 = await File.ReadAllTextAsync(extractedFile1, Encoding.UTF8);
        string content2 = await File.ReadAllTextAsync(extractedFile2, Encoding.UTF8);

        Assert.Equal("Hello MSLX Archive!", content1);
        Assert.Equal("Sub folder content", content2);
    }

    [Fact]
    public async Task Decompress_TarXz_Success()
    {
        string sampleTarXz = "/tmp/test_mslx.tar.xz";
        if (!File.Exists(sampleTarXz)) return;

        string extractDir = Path.Combine(_tempDir, "extracted_tar_xz");
        await _archiveService.DecompressAsync(sampleTarXz, extractDir, "utf-8", CancellationToken.None);

        string extractedFile = Path.Combine(extractDir, "tar_xz_test.txt");
        Assert.True(File.Exists(extractedFile), $"解压后文件应当存在: {extractedFile}");
        string content = await File.ReadAllTextAsync(extractedFile, Encoding.UTF8);
        Assert.Equal("Hello from tar.xz!", content);
    }

    [Fact]
    public async Task Decompress_ZipSlipAttack_Prevented()
    {
        // 构造一个包含 Zip Slip 相对路径的恶意 Zip
        string maliciousZip = Path.Combine(_tempDir, "malicious.zip");
        using (var fs = File.Create(maliciousZip))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            var normalEntry = zip.CreateEntry("valid.txt");
            using (var w = new StreamWriter(normalEntry.Open()))
            {
                w.Write("normal");
            }

            var evilEntry = zip.CreateEntry("../evil.txt");
            using (var w = new StreamWriter(evilEntry.Open()))
            {
                w.Write("hacked");
            }
        }

        string extractDir = Path.Combine(_tempDir, "sandbox");
        Directory.CreateDirectory(extractDir);

        await _archiveService.DecompressAsync(maliciousZip, extractDir, "utf-8", CancellationToken.None);

        // 校验正常文件被解压，而恶意穿透文件未写入上级目录
        Assert.True(File.Exists(Path.Combine(extractDir, "valid.txt")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "evil.txt")), "Zip Slip 穿透文件不应被解压到解压目录之外");
    }

    [Fact]
    public async Task CompressAndDecompress_EncryptedZip_Success()
    {
        string srcDir = Path.Combine(_tempDir, "enc_source");
        Directory.CreateDirectory(srcDir);

        string secretFile = Path.Combine(srcDir, "secret.txt");
        await File.WriteAllTextAsync(secretFile, "Very confidential server secret!", Encoding.UTF8);

        var filesToCompress = new Dictionary<string, string>
        {
            [secretFile] = "secret.txt"
        };

        string encZipPath = Path.Combine(_tempDir, "encrypted.zip");
        string password = "StrongPassword@2026";

        // 1. 纯 C# AES-256 加密压缩
        await _archiveService.CompressAsync(filesToCompress, encZipPath, password, CancellationToken.None);
        Assert.True(File.Exists(encZipPath));

        // 2. 无密码解压应明确提示需要密码
        string extractDirNoPass = Path.Combine(_tempDir, "extract_no_pass");
        var noPassEx = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await _archiveService.DecompressAsync(encZipPath, extractDirNoPass, "utf-8", null, CancellationToken.None);
        });
        Assert.Contains("加密", noPassEx.Message);

        // 3. 错误密码解压应明确提示密码错误
        string extractDirWrongPass = Path.Combine(_tempDir, "extract_wrong_pass");
        var wrongPassEx = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _archiveService.DecompressAsync(encZipPath, extractDirWrongPass, "utf-8", "WrongPass123", CancellationToken.None);
        });
        Assert.Contains("密码错误或当前文件已加密", wrongPassEx.Message);

        // 4. 正确密码解压成功
        string extractDirCorrect = Path.Combine(_tempDir, "extract_correct");
        await _archiveService.DecompressAsync(encZipPath, extractDirCorrect, "utf-8", password, CancellationToken.None);

        string extractedSecret = Path.Combine(extractDirCorrect, "secret.txt");
        Assert.True(File.Exists(extractedSecret));
        string content = await File.ReadAllTextAsync(extractedSecret, Encoding.UTF8);
        Assert.Equal("Very confidential server secret!", content);
    }

    [Fact]
    public async Task Compress_TarWithPassword_ThrowsNotSupportedException()
    {
        string srcDir = Path.Combine(_tempDir, "tar_source");
        Directory.CreateDirectory(srcDir);
        string dummy = Path.Combine(srcDir, "dummy.txt");
        await File.WriteAllTextAsync(dummy, "test", Encoding.UTF8);

        var files = new Dictionary<string, string> { [dummy] = "dummy.txt" };
        string tarPath = Path.Combine(_tempDir, "test.tar.gz");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await _archiveService.CompressAsync(files, tarPath, "password123", CancellationToken.None);
        });
    }
}
