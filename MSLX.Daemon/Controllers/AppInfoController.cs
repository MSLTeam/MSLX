using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MSLX.Daemon.Controllers;

[ApiController]
public class AppInfoController : ControllerBase
{
    private readonly IHubContext<DaemonUpdateHub> _updateHubContext;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly MCServerService _serverService;

    // 构造函数注入
    public AppInfoController(
            IHubContext<DaemonUpdateHub> updateHubContext,
            IHostApplicationLifetime appLifetime,
            MCServerService serverService)
    {
        _updateHubContext = updateHubContext;
        _appLifetime = appLifetime;
        _serverService = serverService;
    }

    [HttpGet("api/status")]
    public IActionResult GetStatus()
    {
        // 获取中间件截取的用户名
        var currentUserId = User.FindFirst("UserId")?.Value;
        
        string displayName = "未登录用户";
        string displayAvatar = "https://www.mslmc.cn/logo.png";
        var roles = new List<string>();

        if (!string.IsNullOrEmpty(currentUserId))
        {
            var userInfo = IConfigBase.UserList.GetUserById(currentUserId);
            if (userInfo != null)
            {
                displayName = !string.IsNullOrEmpty(userInfo.Name) ? userInfo.Name : userInfo.Username;
                displayAvatar = userInfo.Avatar;

                // 处理权限
                if (string.Equals(userInfo.Role, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    roles.Add("all");
                }
                else
                {
                    roles.Add("user");
                }
            }
            
            string osType;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osType = "Windows";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                osType = "Linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                osType = "macOS";
            }
            else
            {
                osType = RuntimeInformation.OSDescription;
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            var systemInfo = new JObject
            {
                ["netVersion"] = RuntimeInformation.FrameworkDescription,
                ["osType"] = osType,
                ["osVersion"] = RuntimeInformation.OSDescription,
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
                ["hostname"] = Environment.MachineName,
                ["docker"] = IsRunningInContainer(),
            };
            
            var statusData = new JObject
            {
                ["clientName"] = "MSLX Daemon",
                ["version"] = PlatFormServices.GetFormattedVersion(),
                ["id"] = currentUserId,
                ["user"] = displayName,  
                ["username"] = userInfo?.Username ?? "mslx",
                ["avatar"] = displayAvatar,
                ["roles"] = JToken.FromObject(roles),
                ["userIp"] = clientIp,
                ["serverTime"] = DateTime.Now,
                ["targetFrontendVersion"] = new JObject
                {
                    ["desktop"] = "1.0.0",
                    ["panel"] = "1.2.1"
                },
                ["systemInfo"] = systemInfo
            };

            var response = new ApiResponse<JObject>
            {
                Code = 200,
                Message = "MSLX Daemon 状态正常",
                Data = statusData
            };
            return Ok(response);

        }
        else
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "用户信息错误",
            });
        }
    }

    [HttpGet("api/update/info")]
    public async Task<IActionResult> GetUpdateInfoAsync()
    {
        try
        {
            var localVerObj = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version("0.0.0.0");
            HttpService.HttpResponse res = await MSLApi.GetAsync("/query/update?software=MSLX", null);

            if (res.IsSuccessStatusCode)
            {
                JObject remoteJObj = JObject.Parse(res.Content ?? "{}");
                string remoteVerStr = remoteJObj["data"]?["daemonLatestVersion"]?.ToString() ?? "0.0.0";
                if (!Version.TryParse(remoteVerStr, out Version remoteVerObj))
                {
                    remoteVerObj = new Version("0.0.0.0");
                }

                // 版本归一化
                Version normalizedLocal = NormalizeVersion(localVerObj);
                Version normalizedRemote = NormalizeVersion(remoteVerObj);

                // 判定状态
                bool needUpdate = normalizedRemote > normalizedLocal;
                string status = "release"; // 默认为最新正式版

                if (needUpdate)
                {
                    status = "outdated";
                }
                else if (normalizedLocal > normalizedRemote)
                {
                    status = "beta"; // 本地版本比服务器还新，说明是测试版
                }

                var responseData = new
                {
                    needUpdate,
                    currentVersion = localVerObj.ToString(),
                    latestVersion = remoteVerStr,
                    status, // release / beta / outdated
                    log = remoteJObj["data"]?["log"]?.ToString()
                };

                return Ok(new ApiResponse<object>()
                {
                    Code = 200,
                    Message = "获取更新信息成功",
                    Data = responseData
                });
            }
            else
            {
                return BadRequest(new ApiResponse<object>()
                {
                    Code = 500,
                    Message = "获取更新信息失败: " + res.StatusCode,
                });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "获取更新信息失败: " + ex.Message,
            });
        }
    }

    // 辅助方法：版本归一化
    private Version NormalizeVersion(Version v)
    {
        return new Version(
            v.Major < 0 ? 0 : v.Major,
            v.Minor < 0 ? 0 : v.Minor,
            v.Build < 0 ? 0 : v.Build,
            v.Revision < 0 ? 0 : v.Revision
        );
    }

    [HttpGet("api/update/download")]
    public async Task<IActionResult> GetUpdateDownloadLinkAsync()
    {
        try
        {
            HttpService.HttpResponse res = await MSLApi.GetAsync($"/download/update?software=MSLX&system={PlatFormServices.GetOs().Replace("MacOS","macOS")}&arch={PlatFormServices.GetOsArch().Replace("amd64","x64")}", null);
            if (res.IsSuccessStatusCode)
            {
                JObject remoteJObj = JObject.Parse(res.Content ?? "{}");
                return Ok(new ApiResponse<object>()
                {
                    Code = 200,
                    Message = "获取更新下载链接成功",
                    Data = new
                    {
                        web = remoteJObj["data"]?["web"]?.ToString() ?? "",
                        file = remoteJObj["data"]?["file"]?.ToString() ?? ""
                    }
                });
            }
            else
            {
                return BadRequest(new ApiResponse<object>()
                {
                    Code = 400,
                    Message = "获取更新下载链接失败: " + res.StatusCode,
                });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "获取更新下载链接失败: " + ex.Message,
            });
        }
    }

#region 更新自身程序

    /// <summary>
    /// 检测是否在 Docker 容器内
    /// </summary>
    private static bool IsRunningInContainer()
    {
        var inContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        return inContainer != null && inContainer.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    [HttpPost("api/update")]
    [Authorize(Roles = "admin")]
    public IActionResult PostUpdate([FromQuery] bool autoRestart = true)
    {
        // 环境预检
        if (IsRunningInContainer())
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "守护进程运行在虚拟容器内，无法通过此方式更新，请使用 Docker Pull 命令更新镜像！",
            });
        }

        // 运行状态预检
        if (_serverService.HasRunningServers())
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "无法更新：当前有服务器实例正在运行！\n请先停止所有服务器实例后再尝试更新。",
            });
        }

        // 启动后台更新任务
        _ = Task.Run(async () => await PerformUpdateProcessAsync(autoRestart));

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "更新请求已接收，后台正在执行下载与安装，请留意更新进度通知。",
        });
    }

    /// <summary>
    /// 后台执行完整的更新流程
    /// </summary>
    private async Task PerformUpdateProcessAsync(bool autoRestart)
    {
        try
        {
            await SendUpdateProgressAsync(0, "0 KB/s", "downloading", "正在获取版本信息...");

            // 获取系统信息
            string osParam = PlatFormServices.GetOs().Replace("MacOS", "macOS");
            string archParam = PlatFormServices.GetOsArch().Replace("amd64", "x64");

            // 获取下载直链
            HttpService.HttpResponse res = await MSLApi.GetAsync($"/download/update?software=MSLX&system={osParam}&arch={archParam}", null);

            if (!res.IsSuccessStatusCode)
                throw new Exception($"获取下载地址失败: {res.StatusCode}");

            JObject remoteJObj = JObject.Parse(res.Content ?? "{}");
            string downloadUrl = remoteJObj["data"]?["file"]?.ToString() ?? string.Empty;

            if (string.IsNullOrEmpty(downloadUrl))
                throw new Exception("更新服务器返回的下载地址为空");

            // 准备路径
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string archiveExt = isWindows ? ".zip" : ".tar.gz";
            string archivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"update_package{archiveExt}");

            // 定义解压后的新文件临时名称
            string currentExeName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName);
            string newFileTempName = currentExeName + ".new";
            string newFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, newFileTempName);

            // 清理残留
            if (System.IO.File.Exists(archivePath)) System.IO.File.Delete(archivePath);
            if (System.IO.File.Exists(newFilePath)) System.IO.File.Delete(newFilePath);

            // 下载
            var downloader = new ParallelDownloader(parallelCount: 1);
            var (success, errorMsg) = await downloader.DownloadFileAsync(downloadUrl, archivePath,
                async (progress, speed) =>
                {
                    await SendUpdateProgressAsync(Math.Round(progress, 2), speed, "downloading", "正在下载更新包...");
                }
            );

            if (!success) throw new Exception($"下载失败: {errorMsg}");

            //解压
            await SendUpdateProgressAsync(100, "0 KB/s", "extracting", "下载完成，正在解压核心文件...");
            // 启动更新脚本并自动重启
            await ExtractCoreFile(archivePath, newFilePath, isWindows);

            if (autoRestart)
            {
                await SendUpdateProgressAsync(100, "0 KB/s", "restarting", "准备重启守护进程...");
                await StartUpdateScriptAndExitAsync(newFileTempName, isWindows);
            }
            else
            {
                // 不自动重启，只通知前端更新完成
                await SendUpdateProgressAsync(100, "0 KB/s", "completed", "更新文件准备完成，等待重启...");

                // 发送更新完成通知（包含新文件路径信息）
                await _updateHubContext.Clients.All.SendAsync("UpdateCompleted", new
                {
                    newFilePath = newFileTempName,
                    message = "更新文件已准备完成，请手动重启守护进程以应用更新。",
                    autoRestart = false
                });
            }

            
        }
        catch (Exception ex)
        {
            await _updateHubContext.Clients.All.SendAsync("UpdateFailed", ex.Message);
            Console.WriteLine($"更新失败: {ex}");
        }
    }

    /// <summary>
    /// 解压核心文件
    /// </summary>
    private async Task ExtractCoreFile(string archivePath, string destinationPath, bool isWindows)
    {
        if (isWindows)
        {
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            ?? archive.Entries.FirstOrDefault();

                if (entry == null) throw new Exception("更新包为空或格式错误");

                await entry.ExtractToFileAsync(destinationPath, overwrite: true);
            }
        }
        else
        {
            // 处理 Tar.gz
            using (FileStream fs = System.IO.File.OpenRead(archivePath))
            using (GZipStream gzipStream = new GZipStream(fs, CompressionMode.Decompress))
            {
                System.Formats.Tar.TarReader reader = new System.Formats.Tar.TarReader(gzipStream);
                System.Formats.Tar.TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    if (entry.EntryType == System.Formats.Tar.TarEntryType.Directory) continue;
                    await entry.ExtractToFileAsync(destinationPath, overwrite: true);
                    break;
                }
            }
        }
        System.IO.File.Delete(archivePath);
    }

    /// <summary>
    /// 生成更新脚本，运行脚本，并关闭当前应用
    /// </summary>
    private async Task StartUpdateScriptAndExitAsync(string newFileTempName, bool isWindows)
    {
        string appPath = AppDomain.CurrentDomain.BaseDirectory;
        string scriptPath;

        var currentProcess = Process.GetCurrentProcess();
        string currentModuleFileName = currentProcess.MainModule?.FileName;
        string currentExeName = Path.GetFileName(currentModuleFileName);

        bool isDotnetDll = currentModuleFileName != null && currentModuleFileName.EndsWith(".dll");
        bool isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        if (isWindows)
        {
            // === Windows 逻辑 ===
            await SendUpdateProgressAsync(100, "0 KB/s", "restarting", "解压完成，即将重启应用...");
            await Task.Delay(2000);

            scriptPath = Path.Combine(appPath, "update_script.bat");
            string startCommand = isDotnetDll
                ? $"start \"\" \"dotnet\" \"{currentExeName}\""
                : $"start \"\" \"{currentExeName}\"";

            string batchContent = $@"
@echo off
cd /d ""{appPath}""
timeout /t 3 /nobreak > NUL
echo Installing update...
move /Y ""{newFileTempName}"" ""{currentExeName}""
if %errorlevel% neq 0 exit /b %errorlevel%
REM Clear Env
set ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=
set DOTNET_MODIFIABLE_ASSEMBLIES=
set ASPNETCORE_ENVIRONMENT=Production
{startCommand}
del ""%~f0"" & exit
";
            System.IO.File.WriteAllText(scriptPath, batchContent, System.Text.Encoding.Default);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appPath
            });
        }
        else if (isMacOS)
        {
            // === macOS 逻辑 ===
            await SendUpdateProgressAsync(100, "0 KB/s", "permission_check", "正在请求终端控制权限，请在服务端确认...");

            scriptPath = Path.Combine(appPath, "update_script.sh");
            string executionCmd = isDotnetDll
                ? $"dotnet ./{Path.GetFileName(currentModuleFileName)}"
                : $"./{currentExeName}";

            // Shell 脚本
            string shellContent = $@"
#!/bin/bash
# 切换到脚本所在目录
cd ""$(dirname ""$0"")""

echo ""----------------------------------------""
echo ""[MSLX] 自动更新程序已启动""
echo ""----------------------------------------""
echo ""正在等待旧进程退出...""
sleep 3

echo ""正在覆盖文件...""
mv -f ./{newFileTempName} ./{currentExeName}
chmod +x ./{currentExeName}

# 清除环境
export ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=
export DOTNET_MODIFIABLE_ASSEMBLIES=
export ASPNETCORE_ENVIRONMENT=Production

echo ""正在启动新版本...""
# 直接运行
{executionCmd}

# 运行结束后删除脚本
rm -- ""$0""
";
            shellContent = shellContent.Replace("\r\n", "\n");
            System.IO.File.WriteAllText(scriptPath, shellContent);
            
            Process.Start("/bin/chmod", $"+x \"{scriptPath}\"").WaitForExit();

            // 生成 AppleScript 临时文件
            string appleScriptPath = Path.Combine(appPath, "update_launcher.scpt");
            string appleScriptContent = $@"
tell application ""Terminal""
    do script ""'{scriptPath}'""
    activate
end tell
";
            System.IO.File.WriteAllText(appleScriptPath, appleScriptContent);

            try
            {
                // 执行 osascript (阻塞等待用户授权)
                var osaProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments = $"\"{appleScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                // 等待权限确认完成
                osaProc?.WaitForExit();

                // 权限确认完毕，发送重启信号
                await SendUpdateProgressAsync(100, "0 KB/s", "restarting", "权限已确认，正在重启...");
                await Task.Delay(2000); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update] 唤起终端失败: {ex.Message}");
            }
            finally
            {
                try { System.IO.File.Delete(appleScriptPath); } catch { }
            }
        }
        else
        {
            // === Linux 逻辑 ===
            await SendUpdateProgressAsync(100, "0 KB/s", "restarting", "解压完成，即将重启应用...");
            await Task.Delay(2000);

            scriptPath = Path.Combine(appPath, "update_script.sh");
            string executionCmd = isDotnetDll
                ? $"dotnet ./{Path.GetFileName(currentModuleFileName)}"
                : $"./{currentExeName}";
            
            string serviceName = "mslx"; 

            string shellContent = $@"
#!/bin/bash
cd ""$(dirname ""$0"")""
sleep 3

# 1. 无论 Systemd 是否重启了进程，Linux 都允许我们强制覆盖文件
#    此时：磁盘上是新版，内存里可能是 Systemd 刚拉起的旧版
mv -f ./{newFileTempName} ./{currentExeName}
chmod +x ./{currentExeName}

# 清除临时变量
ASPNETCORE_HOSTINGSTARTUPASSEMBLIES= DOTNET_MODIFIABLE_ASSEMBLIES= ASPNETCORE_ENVIRONMENT=Production

# === Systemd 智能处理 ===
SERVICE_NAME=""{serviceName}""

# 检查是否存在名为 mslx.service 的服务
if command -v systemctl >/dev/null 2>&1 && systemctl list-unit-files --full | grep -q ""$SERVICE_NAME.service""; then
    echo ""Detected Systemd service: $SERVICE_NAME""
    
    # 尝试重启服务 (这将强制加载刚才覆盖的新文件)
    # 即使 Systemd 刚才自动重启了旧版，这一步也会把它杀掉并启动新版
    sudo systemctl restart ""$SERVICE_NAME""
    
    if [ $? -eq 0 ]; then
        echo ""Systemd restart success.""
    else
        echo ""Systemd restart failed (Permission denied?).""
        echo ""WARNING: Systemd has likely auto-restarted the OLD version.""
        echo ""Please run 'sudo systemctl restart $SERVICE_NAME' manually to apply update.""
    fi
    
    # 【关键】只要检测到 Systemd，无论重启成功与否，都直接退出
    # 绝对不要降级到 nohup，否则会导致双重进程冲突！
    rm -- ""$0""
    exit 0
fi

# === 仅在非 Systemd 环境下使用 Nohup ===
echo ""Systemd not detected, falling back to nohup...""
nohup {executionCmd} < /dev/null > /dev/null 2>&1 &

rm -- ""$0""
";
            shellContent = shellContent.Replace("\r\n", "\n");
            System.IO.File.WriteAllText(scriptPath, shellContent);

            Process.Start("/bin/chmod", $"+x \"{scriptPath}\"").WaitForExit();

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appPath
            });
        }
        
        Thread.Sleep(500);
        _appLifetime.StopApplication();
    }

    private async Task SendUpdateProgressAsync(double progress, object speed, string stage, string message)
    {
        await _updateHubContext.Clients.All.SendAsync("UpdateProgress", new
        {
            progress = progress,
            speed = speed,
            stage = stage,
            status = message
        });
    }

    #endregion

    [HttpGet("api/ping")]
    [AllowAnonymous]
    public IActionResult Ping()
    {
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "pong"
        });
    }
}