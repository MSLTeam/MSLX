using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSLX.Daemon.Utils
{
    internal class ProcessHelper
    {
        /// <summary>
        /// 尝试优雅关闭进程
        /// </summary>
        public static bool SendCtrlC(Process process)
        {
            if (process == null || process.HasExited) return false;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: 使用 taskkill 发送关闭信号 (不带 /F)
                    return SendStopSignalWindows(process.Id);
                }
                else
                {
                    // Linux/Mac: 发送 SIGINT (Ctrl+C)
                    return SendCtrlCUnix(process);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool SendStopSignalWindows(int pid)
        {
            try
            {
                // 启动 taskkill 进程来发送关闭信号
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var killer = Process.Start(psi))
                {
                    killer.WaitForExit(2000); // 等待 taskkill 执行完毕
                    return killer.ExitCode == 0; // 0 表示发送成功
                }
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        // --- Linux/Mac 实现 ---
        [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
        private static extern int UnixKill(int pid, int sig);

        private static bool SendCtrlCUnix(Process process)
        {
            try
            {
                UnixKill(process.Id, 2); // 2 = SIGINT (Ctrl+C)
                return true;
            }
            catch { return false; }
        }
    }
}