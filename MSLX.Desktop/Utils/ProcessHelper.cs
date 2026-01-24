using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils
{
    internal class ProcessHelper
    {
        public static async Task<bool> SendCtrlC(Process process)
        {
            if (process == null || process.HasExited)
                return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await SendCtrlCWindows(process);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return SendCtrlCUnix(process);
            }

            return false;
        }

        // Windows 实现
        [DllImport("kernel32.dll")]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? HandlerRoutine, bool Add);
        private delegate bool ConsoleCtrlDelegate(uint CtrlType);

        private async static Task<bool> SendCtrlCWindows(Process process)
        {
            // 临时忽略主进程的 Ctrl+C 信号
            SetConsoleCtrlHandler(null, true);

            try
            {
                FreeConsole();
                if (AttachConsole((uint)process.Id))
                {
                    GenerateConsoleCtrlEvent(0, 0);
                    await Task.Delay(150);
                    FreeConsole();
                    return true;
                }
                return false;
            }
            finally
            {
                // 恢复主进程的 Ctrl+C 处理
                SetConsoleCtrlHandler(null, false);
            }
        }

        // Unix 实现
        [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
        private static extern int UnixKill(int pid, int sig);

        private static bool SendCtrlCUnix(Process process)
        {
            try
            {
                UnixKill(process.Id, 2); // SIGINT = 2
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
