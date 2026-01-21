using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSLX.Desktop.Utils
{
    internal class ProcessHelper
    {
        public static bool SendCtrlC(Process process)
        {
            if (process == null || process.HasExited)
                return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return SendCtrlCWindows(process);
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

        private static bool SendCtrlCWindows(Process process)
        {
            FreeConsole();
            if (AttachConsole((uint)process.Id))
            {
                GenerateConsoleCtrlEvent(0, 0);
                FreeConsole();
                return true;
            }
            return false;
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
