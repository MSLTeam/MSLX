using System.IO.Compression;
using System.Text;
using MSLX.Daemon.Services.DeployServerService;
using MSLX.Daemon.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MSLX.Tests;

public class MrpackTests
{
    [Fact]
    public void InspectMrpack_ListsRootJarsFromOverridesAndServerOverrides()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true, Encoding.UTF8))
        {
            var index = archive.CreateEntry("modrinth.index.json");
            using (var writer = new StreamWriter(index.Open(), Encoding.UTF8))
            {
                writer.Write("{\"formatVersion\":1,\"name\":\"Test Pack\",\"files\":[]}");
            }

            archive.CreateEntry("server-overrides/server.jar");
            archive.CreateEntry("server-overrides/config/server.cfg");
            archive.CreateEntry("overrides/shared.jar");
            archive.CreateEntry("overrides/mods/some-mod.jar");
        }

        using (var reopened = new ZipArchive(stream, ZipArchiveMode.Read, true, Encoding.UTF8))
        {
            var service = new ServerDeploymentService(NullLogger<ServerDeploymentService>.Instance, null!);
            var result = service.InspectMrpackArchive(reopened);

            Assert.NotNull(result);
            Assert.Equal("mrpack", result!["format"]?.ToObject<string>());
            // overrides 与 server-overrides 部署时都会解压到服务端，故两者根目录下的 jar 都是候选；子目录（如 mods）中的 jar 不计入
            var jars = result["jars"]!.ToObject<List<string>>()!;
            Assert.Equal(2, result["count"]?.ToObject<int>());
            Assert.Contains("server.jar", jars);
            Assert.Contains("shared.jar", jars);
            Assert.Equal("Test Pack", result["metadata"]?["name"]?.ToObject<string>());
        }
    }

    [Fact]
    public void InspectMrpack_FallsBackToOverrides()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true, Encoding.UTF8))
        {
            var index = archive.CreateEntry("modrinth.index.json");
            using (var writer = new StreamWriter(index.Open(), Encoding.UTF8))
            {
                writer.Write("{\"formatVersion\":1,\"name\":\"Fallback Pack\",\"files\":[]}");
            }
            archive.CreateEntry("overrides/server.jar");
        }

        using var reopened = new ZipArchive(stream, ZipArchiveMode.Read, true, Encoding.UTF8);
        var service = new ServerDeploymentService(NullLogger<ServerDeploymentService>.Instance, null!);
        var result = service.InspectMrpackArchive(reopened);

        Assert.NotNull(result);
        Assert.Equal("server.jar", result!["jars"]?[0]?.ToObject<string>());
    }

    [Fact]
    public async Task ValidateFileHash_SupportsSha1AndSha512()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mslx-hash-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, "mrpack");
        try
        {
            byte[] content = await File.ReadAllBytesAsync(path);
            string sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(content));
            string sha512 = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(content));

            Assert.True(await FileUtils.ValidateFileHashAsync(path, sha1,
                System.Security.Cryptography.HashAlgorithmName.SHA1));
            Assert.True(await FileUtils.ValidateFileHashAsync(path, sha512,
                System.Security.Cryptography.HashAlgorithmName.SHA512));
            Assert.False(await FileUtils.ValidateFileHashAsync(path, sha1 + "00",
                System.Security.Cryptography.HashAlgorithmName.SHA1));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
