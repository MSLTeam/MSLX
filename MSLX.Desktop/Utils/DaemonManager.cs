using Avalonia;
using Avalonia.Controls;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils
{
    /// <summary>
    /// Daemon程序管理器，负责解压、安装和启动Daemon程序
    /// </summary>
    public static class DaemonManager
    {
        private static Process? DaemonProcess { get; set; }

        /// <summary>
        /// 验证DaemonApiKey
        /// </summary>
        /// <returns>Success：true代表成功，false代表失败或已取消</returns>
        public static async Task<(bool Success, string Message)> VerifyDaemonApiKey()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            // 关闭先前对话框并显示验证中对话框
            DialogService.DialogManager.DismissDialog();
            DialogService.DialogManager.CreateDialog()
                .WithTitle("验证中...")
                .WithContent("请稍候，正在验证Daemon API Key的有效性。")
                .WithActionButton("取消", _ =>
                {
                    cts.Cancel();
                }, true, "Standard")
                .TryShow();
            try
            {
                var (isSuccess, msg, data) = await DaemonAPIService.VerifyDaemonApiKey(cts.Token);
                string clientName = data?["clientName"]?.Value<string>() ?? "Unknown";
                string version = data?["version"]?.Value<string>() ?? "Unknown";
                string serverTime = data?["serverTime"]?.Value<string>() ?? "Unknown";
                string targetFrontendVersion = data?["targetFrontendVersion"]?["desktop"]?.Value<string>() ?? "0.0.0";
                Version targetVersion = Version.Parse(targetFrontendVersion);

                DialogService.DialogManager.DismissDialog();
                if (isSuccess)
                {
                    ConfigStore.Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                    // 截取前三位版本号进行比较
                    Version targetVersionTrimmed = new Version(targetVersion.Major, targetVersion.Minor, targetVersion.Build);
                    Version currentVersionTrimmed = new(ConfigStore.Version.Major, ConfigStore.Version.Minor, ConfigStore.Version.Build);

                    // 比较版本号
                    if (currentVersionTrimmed != targetVersionTrimmed)
                    {
                        DialogService.ToastManager.CreateToast()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Warning)
                            .WithTitle("与守护程序的兼容性警告")
                            .WithContent($"您的版本号({currentVersionTrimmed})与守护程序要求的版本号({targetVersionTrimmed})不一致，可能会出现兼容性问题！")
                            .Dismiss().After(TimeSpan.FromSeconds(10))
                            .Queue();
                    }

                    // 验证成功
                    DialogService.ToastManager.CreateToast()
                                .WithTitle(msg)
                                .WithContent(new TextBlock
                                {
                                    Text = $"Client Name: {clientName}\nVersion: {version}\nServer Time: {serverTime}",
                                })
                                .Dismiss().After(TimeSpan.FromSeconds(5))
                                .Queue();

                    return (true, "连接成功！");
                }
                else
                {
                    // 验证失败，提示用户并让其重新输入
                    // API Key无效
                    ConfigStore.DaemonApiKey = string.Empty;
                    return (false, "API Key无效");
                }
            }
            catch (OperationCanceledException)
            {
                ConfigStore.DaemonApiKey = string.Empty;
                ConfigStore.DaemonAddress = string.Empty;
                DialogService.DialogManager.DismissDialog();
                return (false, "验证已取消");
            }
            catch (Exception ex)
            {
                ConfigStore.DaemonApiKey = string.Empty;
                ConfigStore.DaemonAddress = string.Empty;
                return (false, $"请检查MSLX守护进程端连接地址是否有效！" + ex.Message);
            }
        }

        /// <summary>
        /// 解压并启动Daemon程序
        /// </summary>
        /// <param name="zipFilePath">下载的压缩包路径</param>
        /// <returns>是否成功启动</returns>
        public static async Task<(bool Success, string Message)> UnzipAndStartDaemon(string zipFilePath)
        {
            try
            {
                // 验证文件是否存在
                if (!File.Exists(zipFilePath))
                {
                    return (false, "压缩包文件不存在！");
                }

                // 获取解压目标路径
                string daemonPath = ConfigService.GetAppDataPath();

                // 确保目标目录存在
                if (!Directory.Exists(daemonPath))
                {
                    Directory.CreateDirectory(daemonPath);
                }

                // 解压文件
                await ZipFile.ExtractToDirectoryAsync(zipFilePath, daemonPath, overwriteFiles: true);

                // 查找并启动Daemon可执行文件
                var (started, message) = await StartDaemon(daemonPath);

                // 清理下载的压缩包
                try
                {
                    File.Delete(zipFilePath);
                }
                catch { }

                return (started, message);
            }
            catch (Exception ex)
            {
                return (false, $"解压或启动Daemon失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动Daemon程序
        /// </summary>
        /// <param name="daemonPath">Daemon程序所在目录</param>
        /// <returns>是否成功启动</returns>
        public static async Task<(bool Success, string Message)> StartDaemon(string daemonPath)
        {
            try
            {
                // 确保目标目录存在
                if (!Directory.Exists(daemonPath))
                {
                    Directory.CreateDirectory(daemonPath);
                }

                // 根据操作系统确定可执行文件名
                string executableName = GetDaemonExecutableName();

                // 查找可执行文件
                string executablePath = FindExecutable(daemonPath, executableName);

                if (string.IsNullOrEmpty(executablePath))
                {
                    return (false, $"未找到Daemon可执行文件: {executableName}");
                }

                // 在Linux和macOS上设置执行权限
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    await SetExecutablePermission(executablePath);
                }

                // 启动进程
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments="/nobrowser true",
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                DaemonProcess = Process.Start(processStartInfo);

                if (DaemonProcess == null)
                {
                    return (false, "无法启动Daemon进程");
                }

                // 等待一小段时间确认进程正常启动
                await Task.Delay(1000);

                if (DaemonProcess.HasExited)
                {
                    return (false, $"Daemon进程启动后立即退出，退出代码: {DaemonProcess.ExitCode}");
                }

                return (true, "Daemon启动成功");
            }
            catch (Exception ex)
            {
                return (false, $"启动Daemon失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止正在运行的Daemon进程
        /// </summary>
        public static async Task StopRunningDaemon()
        {
            try
            {
                if (DaemonProcess != null)
                    await ProcessHelper.SendCtrlC(DaemonProcess);
            }
            catch
            {
                Debug.WriteLine("Close DaemonProcess Failed!");
            }
        }

        /// <summary>
        /// 获取Daemon可执行文件名
        /// </summary>
        private static string GetDaemonExecutableName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "MSLX-Daemon.exe";
            }
            else
            {
                return "MSLX-Daemon";
            }
        }

        public static async Task<bool> GetKeyAndLinkDaemon(bool isGetKey = true, bool showDialog = true)
        {
            if (isGetKey)
            {
                // 加载配置文件，尝试自动获取Daemon API Key
                ConfigService.GetDaemonApiKey();
                await Task.Delay(500);
            }
            string errInfo= string.Empty;
            if (!string.IsNullOrEmpty(ConfigStore.DaemonApiKey))
            {
                if (showDialog)
                {
                    var addressBox = new TextBox
                    {
                        Text = ConfigStore.DaemonAddress,
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    var apiKeyBox = new TextBox
                    {
                        Text = ConfigStore.DaemonApiKey,
                    };
                    var dialogContent = new StackPanel
                    {
                        Children =
                    {
                        new TextBlock
                        {
                            Text="将使用以下信息链接守护程序：",
                            Margin=new Thickness(0,0,0,10)
                        },
                        new TextBlock
                        {
                            Text=$"地址：",
                            Margin=new Thickness(0,0,0,5)
                        },
                        addressBox,
                        new TextBlock
                        {
                            Text=$"API Key：",
                            Margin=new Thickness(0,0,0,5)
                        },
                        apiKeyBox
                    }
                    };
                    var dialogBuilder = DialogService.DialogManager.CreateDialog()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                        .WithTitle("链接守护程序")
                        .WithContent(dialogContent);
                    dialogBuilder.Completion = new TaskCompletionSource<bool>();
                    dialogBuilder.WithActionButton("确认并链接", dialog =>
                    {
                        dialogBuilder.Completion.TrySetResult(true);
                    }, true);

                    dialogBuilder.WithActionButton("取消", dialog =>
                    {
                        dialogBuilder.Completion.TrySetResult(false);
                    }, true, "Standard");
                    var dialog = await dialogBuilder.TryShowAsync();
                    if (!dialog)
                    {
                        return false;
                    }
                    ConfigStore.DaemonAddress = addressBox.Text;
                    ConfigStore.DaemonApiKey = apiKeyBox.Text;
                }

                // 验证
                var (isSuccess, err) = await DaemonManager.VerifyDaemonApiKey();
                if (isSuccess)
                {
                    _ = UpdateService.UpdateDaemonApp(false);
                    return true;
                }
                else
                {
                    errInfo = err;
                }
            }
            else
            {
                errInfo = "未获取到Daemon API Key！";
            }

            DialogService.DialogManager.DismissDialog();
            // 未获取到密钥或验证失败
            var dialogBuilder1 = DialogService.DialogManager.CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                .WithTitle("验证失败！")
                .WithContent($"验证失败：{errInfo}\n请重试！");
            dialogBuilder1.Completion = new TaskCompletionSource<bool>();
            dialogBuilder1.WithActionButton("重试", dialog =>
            {
                dialogBuilder1.Completion.TrySetResult(true);
            }, true);

            dialogBuilder1.WithActionButton("取消", dialog =>
            {
                dialogBuilder1.Completion.TrySetResult(false);
            }, true);
            var dialog1 = await dialogBuilder1.TryShowAsync();
            if (dialog1)
            {
                return await GetKeyAndLinkDaemon();
            }

            return false;
        }

        /// <summary>
        /// 获取Daemon进程名（不含扩展名）
        /// </summary>
        private static string GetDaemonProcessName()
        {
            return "MSLX-Daemon";
        }

        public static Process? FindDaemonProcess()
        {
            var processes = Process.GetProcessesByName(GetDaemonProcessName());
            return processes?.FirstOrDefault();
        }

        /// <summary>
        /// 在目录中查找可执行文件
        /// </summary>
        private static string FindExecutable(string directory, string executableName)
        {
            try
            {
                string directPath = Path.Combine(directory, executableName);
                if (File.Exists(directPath))
                {
                    return directPath;
                }
                else
                    return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 为文件设置可执行权限（Linux/macOS）
        /// </summary>
        private static async Task SetExecutablePermission(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{filePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                }
                catch
                {
                    // 权限设置失败，忽略错误
                }
            }
        }

        /// <summary>
        /// 检查Daemon是否正在运行
        /// </summary>
        public static bool IsDaemonRunning()
        {
            try
            {
                string processName = GetDaemonProcessName();
                var processes = Process.GetProcessesByName(processName);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
