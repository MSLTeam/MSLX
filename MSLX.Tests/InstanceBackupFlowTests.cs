using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Services.InstanceServices;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.Tests.Fakes;
using Newtonsoft.Json.Linq;
using System.IO.Compression;

namespace MSLX.Tests;

/// <summary>
/// 备份主流程测试：通过假命令通道断言 save-off → save-all → 压缩 → save-on 的指令顺序。
/// 注意：IConfigBase.ServerList 是静态全局配置，测试会向测试输出目录下的
/// DaemonData/Configs/ServerList.json 播种，每个用例结束后恢复原文件内容与静态字段。
/// </summary>
[Collection("GlobalConfig")]
public class InstanceBackupFlowTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), $"mslx-backupflow-test-{Guid.NewGuid():N}");

    private readonly string _serverListFile =
        Path.Combine(IConfigBase.GetAppConfigPath(), "ServerList.json");

    private ServerListConfig? _oldServerList;
    private ServerListConfig? _seededConfig;
    private string? _originalServerListJson;

    private (InstanceBackupService svc, FakeCommandChannel mc, FakeMSLXEvents events, InstanceConsoleService console) CreateSut()
    {
        var events = new FakeMSLXEvents();
        var mc = new FakeCommandChannel();
        var console = new InstanceConsoleService(new FakeHubContext<InstanceConsoleHub>(), events);
        var svc = new InstanceBackupService(
            NullLogger<InstanceBackupService>.Instance,
            events,
            new InstanceStateStore(),
            console,
            mc, mc);
        return (svc, mc, events, console);
    }

    private void SeedServer(McServerInfo.ServerInfo server)
    {
        _oldServerList = IConfigBase.ServerList;
        _originalServerListJson = File.Exists(_serverListFile) ? File.ReadAllText(_serverListFile) : null;

        _seededConfig = new ServerListConfig();
        _seededConfig.WriteServerList(new JArray(JObject.FromObject(server)));
        IConfigBase.ServerList = _seededConfig;
    }

    public void Dispose()
    {
        // 恢复失败必须"响"：静默失败意味着静态配置/配置文件被污染，会影响后续测试
        try { IConfigBase.ServerList = _oldServerList!; }
        catch (Exception ex) { Console.WriteLine($"[测试清理] 恢复 IConfigBase.ServerList 失败: {ex}"); }

        try { _seededConfig?.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"[测试清理] 释放播种配置失败: {ex}"); }

        try
        {
            if (_originalServerListJson != null) File.WriteAllText(_serverListFile, _originalServerListJson);
            else if (File.Exists(_serverListFile)) File.Delete(_serverListFile);
        }
        catch (Exception ex) { Console.WriteLine($"[测试清理] 恢复 ServerList.json 失败: {ex}"); }

        try { Directory.Delete(_tempRoot, true); }
        catch (Exception ex) { Console.WriteLine($"[测试清理] 删除临时目录失败: {ex}"); }
    }

    private static McServerInfo.ServerInfo MakeServer(int id, string baseDir) => new()
    {
        ID = id,
        Name = $"test-{id}",
        Base = baseDir,
        Java = "java",
        Core = "server.jar",
        BackupDelay = 0,
        BackupPath = "MSLX://Backup/Instance"
    };

    [Fact]
    public async Task Backup_RunningJavaServer_ExecutesSaveSequenceAndProducesZip()
    {
        // 准备一个有 world 存档的实例目录
        string baseDir = Path.Combine(_tempRoot, "srv");
        Directory.CreateDirectory(Path.Combine(baseDir, "world"));
        File.WriteAllText(Path.Combine(baseDir, "world", "level.dat"), "data");

        SeedServer(MakeServer(990001, baseDir));
        var (svc, mc, events, console) = CreateSut();
        mc.Running = true;
        var context = new ServerContext();

        await svc.BackupServer(990001, context);

        // 指令顺序：save-off → save-all →（压缩）→ save-on
        var cmds = mc.Commands;
        int iOff = cmds.IndexOf("save-off");
        int iAll = cmds.IndexOf("save-all");
        int iOn = cmds.IndexOf("save-on");
        Assert.True(iOff >= 0 && iAll > iOff && iOn > iAll,
            $"指令顺序错误: {string.Join(", ", cmds)}");
        Assert.Equal("save-on", cmds[^1]); // finally 兜底恢复自动保存

        // 产物：zip 存在且包含存档文件
        var zips = Directory.GetFiles(Path.Combine(baseDir, "mslx-backups"), "mslx-backup_*.zip");
        Assert.Single(zips);
        using (var zip = ZipFile.OpenRead(zips[0]))
        {
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("level.dat"));
        }

        // 事件：完成一次，无失败
        Assert.Single(events.CompletedBackups);
        Assert.Empty(events.FailedBackups);

        // 控制台日志包含成功提示
        Assert.Contains(console.GetLogs(context), l => l.Contains("存档备份成功"));
    }

    [Fact]
    public async Task Backup_StoppedServer_SendsNoCommandsButStillProducesZip()
    {
        string baseDir = Path.Combine(_tempRoot, "srv");
        Directory.CreateDirectory(Path.Combine(baseDir, "world"));
        File.WriteAllText(Path.Combine(baseDir, "world", "level.dat"), "data");

        SeedServer(MakeServer(990002, baseDir));
        var (svc, mc, events, console) = CreateSut();
        mc.Running = false;

        await svc.BackupServer(990002, new ServerContext());

        // 服务器未运行：不发送任何游戏内指令
        Assert.Empty(mc.Commands);

        // 仍然完成压缩
        var zips = Directory.GetFiles(Path.Combine(baseDir, "mslx-backups"), "mslx-backup_*.zip");
        Assert.Single(zips);
        Assert.Single(events.CompletedBackups);
        Assert.Empty(events.FailedBackups);
    }

    [Fact]
    public async Task Backup_NoWorldFolders_PublishesFailedAndResumesSave()
    {
        // 没有任何 world/world_nether/world_the_end 目录
        string baseDir = Path.Combine(_tempRoot, "srv");
        Directory.CreateDirectory(baseDir);

        SeedServer(MakeServer(990003, baseDir));
        var (svc, mc, events, console) = CreateSut();
        mc.Running = true;

        await svc.BackupServer(990003, new ServerContext());

        // 失败事件发布，无完成事件；该失败来源不带异常对象（与 catch 路径区分）
        Assert.Single(events.FailedBackups);
        Assert.Null(events.FailedBackups[0].Exception);
        Assert.Contains("未找到任何世界存档文件夹", events.FailedBackups[0].ErrorMessage);
        Assert.Empty(events.CompletedBackups);

        // 未产出 zip
        string backupDir = Path.Combine(baseDir, "mslx-backups");
        Assert.False(Directory.Exists(backupDir) &&
                     Directory.GetFiles(backupDir, "mslx-backup_*.zip").Length > 0);

        // 运行中的服务器：save-off/save-all 已发送，失败后恢复 save-on，且 finally 兜底再发一次
        int iOff = mc.Commands.IndexOf("save-off");
        int iOn = mc.Commands.IndexOf("save-on");
        Assert.True(iOff >= 0 && iOn > iOff,
            $"失败后应恢复自动保存: {string.Join(", ", mc.Commands)}");
        Assert.Equal("save-on", mc.Commands[^1]);
    }

    [Fact]
    public async Task Backup_CancelledByPlugin_NoCommandsNoZipNoCompletion()
    {
        string baseDir = Path.Combine(_tempRoot, "srv");
        Directory.CreateDirectory(Path.Combine(baseDir, "world"));
        File.WriteAllText(Path.Combine(baseDir, "world", "level.dat"), "data");

        SeedServer(MakeServer(990004, baseDir));
        var (svc, mc, events, console) = CreateSut();
        mc.Running = true;
        events.CancelNextBackupStarting = true; // 模拟插件在 BackupStarting 事件中取消

        await svc.BackupServer(990004, new ServerContext());

        // 取消后：零压缩、无完成/失败事件；唯一的指令是 finally 兜底发送的 save-on
        //（恢复自动保存的兜底不区分取消/失败/成功，此处固化该行为）
        Assert.Equal(new[] { "save-on" }, mc.Commands);
        Assert.Single(events.StartedBackups);
        Assert.True(events.StartedBackups[0].Cancel);
        Assert.Empty(events.CompletedBackups);
        Assert.Empty(events.FailedBackups);
        Assert.False(Directory.Exists(Path.Combine(baseDir, "mslx-backups")));
    }
}
