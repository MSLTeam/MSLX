using MSLX.Daemon.Services.InstanceServices;
using System.IO.Compression;

namespace MSLX.Tests;

public class InstanceBackupServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public InstanceBackupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mslx-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    private string CreateWorldFolder()
    {
        // 模拟一个 MC 存档目录结构
        string world = Path.Combine(_tempRoot, "world");
        Directory.CreateDirectory(Path.Combine(world, "region"));
        File.WriteAllText(Path.Combine(world, "level.dat"), "level-data");
        File.WriteAllText(Path.Combine(world, "region", "r.0.0.mca"), "chunk-data");
        File.WriteAllText(Path.Combine(world, "session.lock"), "lock");
        return world;
    }

    [Fact]
    public async Task CompressFolder_CompressesFilesWithRelativePaths()
    {
        string world = CreateWorldFolder();
        string zipPath = Path.Combine(_tempRoot, "test.zip");

        await using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await InstanceBackupService.CompressFolder(_tempRoot, world, archive);
        }

        using var zip = ZipFile.OpenRead(zipPath);
        var entryNames = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entryNames, n => n.EndsWith("level.dat"));
        Assert.Contains(entryNames, n => n.Contains("region") && n.EndsWith("r.0.0.mca"));
    }

    [Fact]
    public async Task CompressFolder_ExcludesSessionLock()
    {
        string world = CreateWorldFolder();
        string zipPath = Path.Combine(_tempRoot, "test.zip");

        await using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await InstanceBackupService.CompressFolder(_tempRoot, world, archive);
        }

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("session.lock"));
    }

    [Fact]
    public async Task CompressFolder_EntryContentMatchesSource()
    {
        string world = CreateWorldFolder();
        string zipPath = Path.Combine(_tempRoot, "test.zip");

        await using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await InstanceBackupService.CompressFolder(_tempRoot, world, archive);
        }

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.First(e => e.FullName.EndsWith("level.dat"));
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("level-data", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CompressFolder_NestedSubfolders_AreRecursed()
    {
        string world = CreateWorldFolder();
        string deep = Path.Combine(world, "data", "advancements");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "a.json"), "{}");
        string zipPath = Path.Combine(_tempRoot, "test.zip");

        await using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await InstanceBackupService.CompressFolder(_tempRoot, world, archive);
        }

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("a.json"));
    }
}
