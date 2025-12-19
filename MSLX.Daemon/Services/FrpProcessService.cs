using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Formats.Tar;
using System.Text;
using Downloader;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services;

public class FrpProcessService
{
    private readonly ILogger<FrpProcessService> _logger;
    private readonly IHubContext<FrpConsoleHub> _hubContext; 
    private readonly IHostApplicationLifetime _appLifetime; 

    // 用户http请求的httpclient
    private static readonly HttpClient _apiClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public class FrpContext
    {
        public Process? Process { get; set; }
        public ConcurrentQueue<string> Logs { get; set; } = new();
        public bool IsInitializing { get; set; } = false; // 初始化中
    }

    private readonly ConcurrentDictionary<int, FrpContext> _activeProcesses = new();
    private const int MaxLogLines = 200;
    
    private readonly string _frpcExecutablePath;
    private readonly string _toolsDir;

    public FrpProcessService(ILogger<FrpProcessService> logger, IHubContext<FrpConsoleHub> hubContext, IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _hubContext = hubContext;
        _appLifetime = appLifetime;
        
        string baseDir = IConfigBase.GetAppDataPath();
        string exeName = PlatFormServices.GetOs() == "Windows" ? "frpc.exe" : "frpc";
        
        _toolsDir = Path.Combine(baseDir, "DaemonData", "Tools");
        _frpcExecutablePath = Path.Combine(_toolsDir, exeName);

        _appLifetime.ApplicationStopping.Register(StopAllFrp);
    }

    public bool IsFrpRunning(int id)
    {
        if (_activeProcesses.TryGetValue(id, out var context))
        {
            // 初始化=运行中
            if (context.IsInitializing) return true;

            if (context.Process != null && !context.Process.HasExited)
            {
                return true;
            }
            // 只有确实退出了才移除
            _activeProcesses.TryRemove(id, out _);
        }
        return false;
    }

    /// <summary>
    /// 启动 FRP 进程 (非阻塞模式)
    /// </summary>
    public (bool success, string message) StartFrp(int id)
    {
        if (IsFrpRunning(id)) return (false, "该隧道已经在运行中或正在启动中");
        
        var frpConfig = IConfigBase.FrpList.GetFrpConfig(id);
        if (frpConfig == null) return (false, "找不到指定的配置");

        // 占位
        var context = new FrpContext { IsInitializing = true };
        _activeProcesses[id] = context;

        // 后台任务启动Frpc
        _ = Task.Run(async () => await InternalStartProcessAsync(id, context, frpConfig));

        // 返回数据
        if (!File.Exists(_frpcExecutablePath))
        {
            return (true, "核心文件缺失，正在后台自动下载并启动，请留意日志窗口...");
        }
        return (true, "正在启动隧道...");
    }
    
    // 下载启动Frpc
    private async Task InternalStartProcessAsync(int id, FrpContext context, JObject frpConfig)
    {
        try
        {
            if (!File.Exists(_frpcExecutablePath)) 
            {
                RecordLog(id, context, ">>> [MSLX-FrpcService] 检测到 Frpc 核心文件缺失，准备开始自动下载...");
                bool downloadSuccess = await DownloadFrpcAsync(id, context);
                
                if (!downloadSuccess)
                {
                    RecordLog(id, context, ">>> [MSLX-FrpcService] 核心文件下载失败，启动终止。");
                    _activeProcesses.TryRemove(id, out _); // 移除占位
                    return;
                }
                RecordLog(id, context, ">>> [MSLX-FrpcService] 核心文件准备就绪，正在启动...");
            }

            // 配置文件
            string configType = frpConfig["ConfigType"]?.ToString() ?? "ini";
            string configFolder = Path.Combine(IConfigBase.GetAppDataPath(), "DaemonData", "Configs", "Frpc", id.ToString());
            string configFilePath = Path.Combine(configFolder, $"frpc.{configType}");
        
            if (!File.Exists(configFilePath))
            {
                RecordLog(id, context, $">>> [MSLX-FrpcService] 配置文件不存在: {configFilePath}");
                _activeProcesses.TryRemove(id, out _);
                return;
            }

            // 给个运行权限
            ExecutePermission.GrantExecutePermission(_frpcExecutablePath);

            // 开始启动！
            var startInfo = new ProcessStartInfo
            {
                FileName = _frpcExecutablePath,
                Arguments = $"-c \"{configFilePath}\"",
                RedirectStandardOutput = true, 
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var process = new Process { StartInfo = startInfo };
            
            // 绑定事件
            process.OutputDataReceived += (sender, e) => RecordLog(id, context, e.Data);
            process.ErrorDataReceived += (sender, e) => RecordLog(id, context, e.Data);
            
            RecordLog(id, context, "[MSLX] 正在启动 Frpc 进程...");

            // 启动
            if (process.Start())
            {
                context.Process = process;
                context.IsInitializing = false; // 初始化完成
                
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                _logger.LogInformation($"FRP 隧道 [{id}] 启动成功，PID: {process.Id}");
            }
            else
            {
                RecordLog(id, context, ">>> [MSLX-FrpcService] 进程启动失败！");
                _activeProcesses.TryRemove(id, out _);
            }
        }
        catch (Exception ex)
        {
            RecordLog(id, context, $">>> [MSLX-FrpcService] 启动流程发生未捕获异常: {ex.Message}");
            _logger.LogError(ex, $"FRP 隧道 [{id}] 启动异常");
            _activeProcesses.TryRemove(id, out _);
        }
    }
    
    // 这里实现下载Frpc逻辑
private async Task<bool> DownloadFrpcAsync(int id, FrpContext context)
{
    try
    {
        string os = PlatFormServices.GetOs();
        string arch = PlatFormServices.GetOsArch();
        
        // 不知道为什么还要兼容一下win7
        bool isWin7 = false;
        if (os == "Windows")
        {
            // 内核6.1
            var winVer = Environment.OSVersion.Version;
            if (winVer.Major == 6 && winVer.Minor == 1)
            {
                isWin7 = true;
            }
        }

        RecordLog(id, context, $">>> [MSLX-FrpcService] 正在获取版本信息 (System: {os}, Arch: {arch}{(isWin7 ? ", Win7 Legacy" : "")})...");

        var jsonStr = await _apiClient.GetStringAsync("https://user.mslmc.net/api/frp/download");
        var apiResp = JObject.Parse(jsonStr);

        // 获取 cli 列表数组
        var cliList = apiResp["data"]?["cli"]?.ToObject<JArray>();
        if (cliList == null || !cliList.Any())
        {
             RecordLog(id, context, ">>> [MSLX-FrpcService] 错误：API 返回的版本列表为空。");
             return false;
        }
        
        JToken? targetNode = null;

        if (isWin7)
        {
            RecordLog(id, context, ">>> [MSLX-FrpcService] 检测到 Windows 7 系统，正在查找 Frpc 兼容版本 (0.51.2)...");
            RecordLog(id, context, ">>> [MSLX-FrpcService] 此系统已经过时，建议你您升级到 Windows 10或者更高版本使用～");
            // 查找0.51.2版本
            targetNode = cliList.FirstOrDefault(x => x["version"]?.ToString() == "0.51.2");
            
            if (targetNode == null)
            {
                RecordLog(id, context, ">>> [MSLX-FrpcService] 警告：未找到 0.51.2 兼容版本，启动取消！");
                return false;
            }
        }
        else
        {
            // 读取第一个 是最新版
            targetNode = cliList.First();
        }

        // 根据系统和架构获取下载节点
        var downloadNode = targetNode?["download"]?[os]?[arch];
        
        if (downloadNode == null)
        {
            RecordLog(id, context, $">>> [MSLX-FrpcService] 错误：在版本 {targetNode?["version"]} 中未找到适配当前系统的下载项。");
            return false;
        }

        string downloadUrl = downloadNode["url"]?.ToString() ?? "";
        string version = targetNode?["version"]?.ToString() ?? "Unknown";

        if (string.IsNullOrEmpty(downloadUrl)) return false;

        RecordLog(id, context, $">>> [MSLX-FrpcService] 获取成功 (Ver: {version})，准备下载...");

        // 临时文件名
        string tempFileName = Path.Combine(_toolsDir, $"frpc_temp_{Guid.NewGuid()}");
        if (downloadUrl.Contains(".zip")) tempFileName += ".zip";
        else if (downloadUrl.Contains(".tar.gz")) tempFileName += ".tar.gz";

        if (!Directory.Exists(_toolsDir)) Directory.CreateDirectory(_toolsDir);

        // 配置下载器
        var downloadOpt = new DownloadConfiguration { ChunkCount = 8, ParallelDownload = true };
        var downloader = new DownloadService(downloadOpt);
        
        DateTime lastReportTime = DateTime.MinValue;
        const int throttleMs = 1000; // 推送延迟

        downloader.DownloadProgressChanged += async (s, e) =>
        {
            if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds > throttleMs)
            {
                lastReportTime = DateTime.UtcNow;
                double percent = Math.Round(e.ProgressPercentage, 1);
                // 推送日志
                await _hubContext.Clients.Group(id.ToString()).SendAsync("ReceiveLog", $">>> [MSLX-FrpcService] 下载 Frpc 核心中... {percent}% ({ConvertBytes(e.ReceivedBytesSize)}/{ConvertBytes(e.TotalBytesToReceive)})");
            }
        };

        bool isDownloadSuccess = false;
        string downloadError = "";

        downloader.DownloadFileCompleted += (s, e) =>
        {
            if (e.Cancelled) downloadError = "下载被取消";
            else if (e.Error != null) downloadError = e.Error.Message;
            else isDownloadSuccess = true;
        };

        // 开始下载
        await downloader.DownloadFileTaskAsync(downloadUrl, tempFileName);

        if (!isDownloadSuccess)
        {
            RecordLog(id, context, $">>> [MSLX-FrpcService] 下载失败: {downloadError}");
            try { File.Delete(tempFileName); } catch { }
            return false;
        }

        RecordLog(id, context, ">>> [MSLX-FrpcService] 下载完成，正在解压...");

        bool extractSuccess = ExtractCoreFile(tempFileName, _toolsDir, os);
        try { File.Delete(tempFileName); } catch { }

        if (extractSuccess)
        {
            RecordLog(id, context, ">>> [MSLX-FrpcService] Frpc 核心文件安装成功！");
            return true;
        }
        else
        {
            RecordLog(id, context, ">>> [MSLX-FrpcService] Frpc 安装失败：未在压缩包中找到核心文件。");
            return false;
        }
    }
    catch (Exception ex)
    {
        RecordLog(id, context, $">>> [MSLX-FrpcService] 自动下载流程异常: {ex.Message}");
        return false;
    }
}

    /// <summary>
    /// 解压逻辑：支持 Zip 和 Tar.gz
    /// </summary>
    private bool ExtractCoreFile(string archivePath, string destDir, string os)
    {
        string targetFileName = os == "Windows" ? "frpc.exe" : "frpc";
        string destPath = Path.Combine(destDir, targetFileName);
        bool found = false;

        try
        {
            using var fs = File.OpenRead(archivePath);

            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Equals(targetFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(destPath, overwrite: true);
                        found = true;
                        break;
                    }
                }
            }
            else 
            {
                // Linux/Mac Tar.gz
                using var gzip = new GZipStream(fs, CompressionMode.Decompress);
                using var tar = new TarReader(gzip);
                
                TarEntry? entry;
                while ((entry = tar.GetNextEntry()) != null)
                {
                    if (entry.DataStream == null) continue;
                    string entryName = Path.GetFileName(entry.Name); // Flatten path
                    if (entryName.Equals(targetFileName, StringComparison.Ordinal))
                    {
                        entry.ExtractToFile(destPath, overwrite: true);
                        found = true;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解压核心文件时出错");
        }
        return found;
    }

    public bool StopFrp(int id)
    {
        if (_activeProcesses.TryRemove(id, out var context))
        {
            try
            {
                if (context.Process != null && !context.Process.HasExited)
                {
                    context.Process.Kill(); 
                    context.Process.WaitForExit(1000); 
                }
                RecordLog(id, context, "[MSLX] Frpc进程已经退出...");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止进程时出错");
            }
        }
        return false;
    }

    public List<string> GetLogs(int id)
    {
        if (_activeProcesses.TryGetValue(id, out var context))
        {
            return context.Logs.ToList();
        }
        return new List<string>();
    }

    private void StopAllFrp()
    {
        if (_activeProcesses.IsEmpty) return;
        foreach (var kvp in _activeProcesses)
        {
            try
            {
                var context = kvp.Value;
                if (context.Process != null && !context.Process.HasExited) context.Process.Kill();
            }
            catch { }
        }
        _activeProcesses.Clear();
    }

    private void RecordLog(int frpId, FrpContext context, string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;
        
        context.Logs.Enqueue(data);
        while (context.Logs.Count > MaxLogLines) context.Logs.TryDequeue(out _);
        _hubContext.Clients.Group(frpId.ToString()).SendAsync("ReceiveLog", data);
    }
    
    private string ConvertBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    /*
    private void GrantExecutePermission(string filePath)
    {
        string os = PlatFormServices.GetOs();
        if (os == "Windows") return;
        try
        {
            var info = new ProcessStartInfo { FileName = "chmod", Arguments = $"+x \"{filePath}\"", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(info);
            proc?.WaitForExit();
        }
        catch { }
    }
    */
}