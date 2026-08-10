using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils;

/// <summary>
/// 为 macOS .app 提供 DMG 自动安装能力。
/// </summary>
internal sealed class MacAppSparkleUpdater : SparkleUpdater
{
    private const int InstallerReadyTimeoutSeconds = 120;

    public MacAppSparkleUpdater(string appCastUrl, ISignatureVerifier signatureVerifier)
        : base(appCastUrl, signatureVerifier)
    {
    }

    protected override async Task RunDownloadedInstaller(string downloadFilePath)
    {
        try
        {
            if (!File.Exists(downloadFilePath) ||
                !await IsValidDiskImageAsync(downloadFilePath))
            {
                UIFactory?.ShowUnknownInstallerFormatMessage(downloadFilePath);
                return;
            }

            string targetAppPath = GetTargetAppPath();
            string targetParentDirectory = Directory.GetParent(targetAppPath)?.FullName
                ?? throw new InvalidOperationException("无法确定应用安装目录。");
            bool requiresAdministrator = !CanWriteToDirectory(targetParentDirectory);
            string workDirectory = Path.Combine(Path.GetTempPath(), $"mslx-update-{Guid.NewGuid():N}");
            string scriptPath = Path.Combine(workDirectory, "install-update.sh");
            string readyFilePath = Path.Combine(workDirectory, "installer-ready");
            string logFilePath = Path.Combine(workDirectory, "install.log");

            Directory.CreateDirectory(workDirectory);
            File.WriteAllText(logFilePath, string.Empty, new UTF8Encoding(false));
            File.WriteAllText(
                scriptPath,
                BuildInstallerScript(
                    downloadFilePath,
                    targetAppPath,
                    workDirectory,
                    readyFilePath,
                    logFilePath,
                    Environment.ProcessId),
                new UTF8Encoding(false));
            if (OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            using Process installerProcess = StartInstallerProcess(
                scriptPath,
                targetAppPath,
                workDirectory,
                readyFilePath,
                requiresAdministrator);

            bool installerReady = await WaitForInstallerReadyAsync(
                installerProcess,
                readyFilePath,
                TimeSpan.FromSeconds(InstallerReadyTimeoutSeconds));
            if (!installerReady)
            {
                TryStopProcess(installerProcess);
                UIFactory?.ShowDownloadErrorMessage(
                    requiresAdministrator
                        ? "未获得安装更新所需的管理员授权，当前版本将继续运行。"
                        : "自动安装程序未能启动，当前版本将继续运行。",
                    AppCastUrl);
                return;
            }

            LogWriter?.PrintMessage("macOS 自动安装程序已就绪，准备退出当前应用。");
            await QuitApplication();
        }
        catch (Exception ex)
        {
            LogWriter?.PrintMessage("启动 macOS 自动安装程序失败：{0}", ex);
            UIFactory?.ShowDownloadErrorMessage(
                $"无法启动自动安装程序：{ex.Message}",
                AppCastUrl);
        }
    }

    private static Process StartInstallerProcess(
        string scriptPath,
        string targetAppPath,
        string workDirectory,
        string readyFilePath,
        bool requiresAdministrator)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("on run argv");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("set installerScript to item 1 of argv");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("set targetApp to item 2 of argv");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("set workDirectory to item 3 of argv");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("set readyFile to item 4 of argv");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("set installCommand to \"/bin/bash \" & quoted form of installerScript");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("set openCommand to \"/usr/bin/open \" & quoted form of targetApp");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("set cleanupCommand to \"/bin/rm -rf \" & quoted form of workDirectory");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("try");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(requiresAdministrator
            ? "do shell script installCommand with administrator privileges"
            : "do shell script installCommand");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("do shell script openCommand");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("do shell script cleanupCommand");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("on error errorMessage");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("try");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("do shell script (\"/bin/test -e \" & quoted form of readyFile)");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("do shell script openCommand");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("end try");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("display alert \"MSLX 更新失败\" message \"无法自动安装更新，已保留当前版本。请重新打开 MSLX 后重试。\"");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("end try");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("end run");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(targetAppPath);
        startInfo.ArgumentList.Add(workDirectory);
        startInfo.ArgumentList.Add(readyFilePath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 macOS 自动安装程序。");
    }

    private static async Task<bool> WaitForInstallerReadyAsync(
        Process installerProcess,
        string readyFilePath,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(readyFilePath))
            {
                return true;
            }

            if (installerProcess.HasExited)
            {
                return false;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private static async Task<bool> IsValidDiskImageAsync(string filePath)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/hdiutil",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("imageinfo");
        process.StartInfo.ArgumentList.Add("-plist");
        process.StartInfo.ArgumentList.Add(filePath);

        try
        {
            if (!process.Start())
            {
                return false;
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask)
                .WaitAsync(TimeSpan.FromSeconds(30));
            return process.ExitCode == 0;
        }
        catch (TimeoutException)
        {
            TryStopProcess(process);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string GetTargetAppPath()
    {
        var macOsDirectory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        string appBundlePath = macOsDirectory.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("无法确定当前应用包路径。");

        if (appBundlePath.StartsWith("/Volumes/", StringComparison.Ordinal))
        {
            return "/Applications/MSLX.app";
        }

        return appBundlePath;
    }

    private static bool CanWriteToDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            string probePath = Path.Combine(directoryPath, $".mslx-write-test-{Guid.NewGuid():N}");
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 安装程序可能已在检查后自行退出，无需继续处理。
        }
    }

    private static string BuildInstallerScript(
        string downloadFilePath,
        string targetAppPath,
        string workDirectory,
        string readyFilePath,
        string logFilePath,
        int parentProcessId)
    {
        string mountDirectory = Path.Combine(workDirectory, "mounted-dmg");
        string stagingAppPath = $"{targetAppPath}.update-{Guid.NewGuid():N}";
        string backupAppPath = $"{targetAppPath}.backup-{Guid.NewGuid():N}";
        var script = new StringBuilder();

        script.AppendLine("#!/bin/bash");
        script.AppendLine("set -u");
        script.AppendLine($"DMG_PATH={ShellQuote(downloadFilePath)}");
        script.AppendLine($"TARGET_APP={ShellQuote(targetAppPath)}");
        script.AppendLine($"MOUNT_DIR={ShellQuote(mountDirectory)}");
        script.AppendLine($"STAGING_APP={ShellQuote(stagingAppPath)}");
        script.AppendLine($"BACKUP_APP={ShellQuote(backupAppPath)}");
        script.AppendLine($"READY_FILE={ShellQuote(readyFilePath)}");
        script.AppendLine($"LOG_FILE={ShellQuote(logFilePath)}");
        script.AppendLine($"PARENT_PID={parentProcessId}");
        script.AppendLine("MOUNTED=0");
        script.AppendLine("exec >>\"$LOG_FILE\" 2>&1");
        script.AppendLine("cleanup_mount() {");
        script.AppendLine("  if [ \"$MOUNTED\" -eq 1 ]; then");
        script.AppendLine("    /usr/bin/hdiutil detach \"$MOUNT_DIR\" -force || true");
        script.AppendLine("  fi");
        script.AppendLine("}");
        script.AppendLine("rollback() {");
        script.AppendLine("  if [ -d \"$BACKUP_APP\" ] && [ ! -e \"$TARGET_APP\" ]; then");
        script.AppendLine("    /bin/mv \"$BACKUP_APP\" \"$TARGET_APP\" || true");
        script.AppendLine("  fi");
        script.AppendLine("}");
        script.AppendLine("fail() {");
        script.AppendLine("  echo \"$1\"");
        script.AppendLine("  rollback");
        script.AppendLine("  exit 1");
        script.AppendLine("}");
        script.AppendLine("trap cleanup_mount EXIT");
        script.AppendLine("/usr/bin/touch \"$READY_FILE\" || exit 1");
        script.AppendLine("COUNTER=0");
        script.AppendLine("while /bin/kill -0 \"$PARENT_PID\" 2>/dev/null; do");
        script.AppendLine("  /bin/sleep 1");
        script.AppendLine("  COUNTER=$((COUNTER + 1))");
        script.AppendLine("  if [ \"$COUNTER\" -ge 120 ]; then");
        script.AppendLine("    fail \"等待旧版 MSLX 退出超时。\"");
        script.AppendLine("  fi");
        script.AppendLine("done");
        script.AppendLine("/bin/mkdir -p \"$MOUNT_DIR\" || fail \"无法创建 DMG 挂载目录。\"");
        script.AppendLine("/usr/bin/hdiutil attach \"$DMG_PATH\" -nobrowse -readonly -mountpoint \"$MOUNT_DIR\" || fail \"无法挂载更新 DMG。\"");
        script.AppendLine("MOUNTED=1");
        script.AppendLine("SOURCE_APP=\"$MOUNT_DIR/MSLX.app\"");
        script.AppendLine("[ -d \"$SOURCE_APP\" ] || fail \"DMG 中未找到 MSLX.app。\"");
        script.AppendLine("/usr/bin/codesign --verify --deep --strict \"$SOURCE_APP\" || fail \"新版 MSLX.app 签名验证失败。\"");
        script.AppendLine("/bin/rm -rf \"$STAGING_APP\" \"$BACKUP_APP\"");
        script.AppendLine("/usr/bin/ditto \"$SOURCE_APP\" \"$STAGING_APP\" || fail \"无法复制新版 MSLX.app。\"");
        script.AppendLine("if [ -e \"$TARGET_APP\" ]; then");
        script.AppendLine("  /bin/mv \"$TARGET_APP\" \"$BACKUP_APP\" || fail \"无法备份当前 MSLX.app。\"");
        script.AppendLine("fi");
        script.AppendLine("/bin/mv \"$STAGING_APP\" \"$TARGET_APP\" || fail \"无法启用新版 MSLX.app。\"");
        script.AppendLine("/bin/rm -rf \"$BACKUP_APP\"");
        script.AppendLine("/usr/bin/hdiutil detach \"$MOUNT_DIR\" -force || true");
        script.AppendLine("MOUNTED=0");
        script.AppendLine("/bin/rm -f \"$DMG_PATH\"");
        script.AppendLine("exit 0");

        return script.ToString();
    }

    private static string ShellQuote(string value)
    {
        return $"'{value.Replace("'", "'\\''")}'";
    }
}
