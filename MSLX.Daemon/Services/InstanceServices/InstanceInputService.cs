using CliWrap;
using CliWrap.Buffered;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;
using Porta.Pty;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 实例输入与命令服务：处理向服务器发送指令、PTY 输入输出、终端尺寸调整等。
/// 剥离自原 MCServerService。
/// </summary>
public class InstanceInputService : IInstanceConsoleService
{
    private readonly ILogger<InstanceInputService> _logger;
    private readonly IMSLXEvents _events;
    private readonly InstanceStateStore _stateStore;
    private readonly InstanceConsoleService _console;

    public InstanceInputService(
        ILogger<InstanceInputService> logger,
        IMSLXEvents events,
        InstanceStateStore stateStore,
        InstanceConsoleService console)
    {
        _logger = logger;
        _events = events;
        _stateStore = stateStore;
        _console = console;
    }

    public bool SendCommand(uint instanceId, string command, bool repeatCommandToLog = false)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            var cmdArgs = new ServerCommandExecutingEventArgs
            {
                InstanceId = instanceId,
                Command = command,
                SentViaRcon = false,
                Timestamp = DateTime.Now
            };
            _events.PublishServerCommandExecuting(cmdArgs);
            if (cmdArgs.Cancel)
            {
                _logger.LogInformation($"[MSLX] 实例 {instanceId} 的命令 [{command}] 已被插件拦截取消。");
                return false;
            }

            try
            {
                lock (context.StateLock)
                {
                    // 子进程溜出来情况的处理
                    if (!context.IsPtyMode && context.IsProcessExited && (!context.IsStdoutClosed || !context.IsStderrClosed))
                    {
                        _console.RecordLog(instanceId, context, ">>> [MSLX-Daemon] 当前服务端处于特殊的进程状态，已脱离MSLX的进程监控。");
                        _console.RecordLog(instanceId, context, ">>> [MSLX-Daemon] 因此目前无法向服务端发送指令，您可以重启后再尝试或在游戏内进行指令输入。");
                        return false;
                    }
                }

                if (context.Process != null && !context.Process.HasExited)
                {
                    bool sentViaRcon = false;
                    var serverInfo = IConfigBase.ServerList.GetServer(instanceId);
                    if (serverInfo != null)
                    {
                        if (serverInfo.RconMode == "mc")
                        {
                            try
                            {
                                string propsPath = Utils.ServerPropertiesPathUtils.ResolveFullPath(serverInfo);
                                if (File.Exists(propsPath))
                                {
                                    var lines = File.ReadAllLines(propsPath);
                                    bool rconEnabled = false;
                                    int rconPort = 25575;
                                    string rconPassword = "";
                                    foreach (var line in lines)
                                    {
                                        if (line.StartsWith("enable-rcon=")) rconEnabled = line.EndsWith("true", StringComparison.OrdinalIgnoreCase);
                                        if (line.StartsWith("rcon.port=")) int.TryParse(line.Replace("rcon.port=", "").Trim(), out rconPort);
                                        if (line.StartsWith("rcon.password=")) rconPassword = line.Replace("rcon.password=", "").Trim();
                                    }

                                    if (rconEnabled && !string.IsNullOrWhiteSpace(rconPassword))
                                    {
                                        using var rcon = new Utils.MinecraftRconClient("127.0.0.1", rconPort, rconPassword);
                                        if (rcon.ConnectAsync().GetAwaiter().GetResult())
                                        {
                                            string response = rcon.SendCommandAsync(command).GetAwaiter().GetResult();
                                            sentViaRcon = true;
                                            if (!string.IsNullOrWhiteSpace(response))
                                            {
                                                _console.RecordLog(instanceId, context, $">>> [RCON] {response}");
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        else if (!string.IsNullOrEmpty(serverInfo.RconMode) && serverInfo.RconMode.Contains(":"))
                        {
                            try
                            {
                                var parts = serverInfo.RconMode.Split(':', 2);
                                if (parts.Length == 2 && int.TryParse(parts[0], out int rconPort))
                                {
                                    string rconPassword = parts[1];
                                    using var rcon = new Utils.MinecraftRconClient("127.0.0.1", rconPort, rconPassword);
                                    if (rcon.ConnectAsync().GetAwaiter().GetResult())
                                    {
                                        string response = rcon.SendCommandAsync(command).GetAwaiter().GetResult();
                                        sentViaRcon = true;
                                        if (!string.IsNullOrWhiteSpace(response))
                                        {
                                            _console.RecordLog(instanceId, context, $">>> [RCON] {response}");
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (!sentViaRcon)
                    {
                        if (context.IsPtyMode && context.PtyConnection != null)
                        {
                            WritePtyCommandClean(context.PtyConnection, command);
                        }
                        else if (context.Process != null)
                        {
                            WriteStandardInputCommand(context.Process, command);
                        }
                    }
                    if (repeatCommandToLog) _console.RecordLog(instanceId, context, $"[MSLX-Daemon] 已发送命令{(sentViaRcon ? "(RCON)" : "")}: {command}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"向服务器 [{instanceId}] 发送命令时出错");
                _console.RecordLog(instanceId, context, $">>> [MSLX-MCServer] 发送命令失败: {ex.Message}");
            }
        }

        return false;
    }

    private static void WritePtyCommandClean(IPtyConnection pty, string command, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var lines = command.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        bool isWindows = OperatingSystem.IsWindows();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string payload;
            if (isWindows)
            {
                payload = "\r\n" + line + "\r\n";
            }
            else
            {
                payload = "\x05\x15" + line + "\n";
            }

            byte[] ptyBytes = encoding.GetBytes(payload);
            pty.WriterStream.Write(ptyBytes, 0, ptyBytes.Length);
        }
        pty.WriterStream.Flush();
    }

    private static void WriteStandardInputCommand(Process process, string command)
    {
        var lines = command.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            process.StandardInput.WriteLine(line);
        }
        process.StandardInput.Flush();
    }

    public bool SendPtyInput(uint instanceId, string data)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            return SendPtyInput(instanceId, bytes);
        }
        return false;
    }

    public bool SendPtyInput(uint instanceId, byte[] data)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            if (context.IsPtyMode && context.PtyConnection != null)
            {
                try
                {
                    context.PtyConnection.WriterStream.Write(data, 0, data.Length);
                    context.PtyConnection.WriterStream.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[PTY] 写入输入数据失败: {ex.Message}");
                    return false;
                }
            }
            else if (context.Process != null && !context.Process.HasExited)
            {
                try
                {
                    string str = Encoding.UTF8.GetString(data);
                    if (str.Contains('\r') || str.Contains('\n'))
                    {
                        var lines = str.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            context.Process.StandardInput.WriteLine(line);
                        }
                        context.Process.StandardInput.Flush();
                    }
                    return true;
                }
                catch { return false; }
            }
        }
        return false;
    }

    public bool ResizePty(uint instanceId, int cols, int rows)
    {
        if (cols > 0 && rows > 0)
        {
            _stateStore.SetPreferredTerminalSize(instanceId, cols, rows);
        }

        if (_stateStore.Get(instanceId) is { } context)
        {
            if (context.IsPtyMode && context.PtyConnection != null)
            {
                try
                {
                    context.PtyConnection.Resize(cols, rows);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[PTY] 调整终端尺寸失败: {ex.Message}");
                    return false;
                }
            }
        }
        return false;
    }

    public bool IsServerPtyMode(uint instanceId)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            return context.IsPtyMode && context.PtyConnection != null;
        }
        return false;
    }

    public List<string> GetLogs(uint instanceId)
    {
        return _console.GetLogs(_stateStore.Get(instanceId));
    }


    public List<string> GetPtyHistory(uint instanceId)
    {
        return _console.GetPtyHistory(_stateStore.Get(instanceId));
    }
}
