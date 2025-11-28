namespace MSLX.Daemon.Utils
{
    public class ExecutePermission
    {
        /// <summary>
        /// 对Win系统，直接返回true，Linux和Mac则进行提权
        /// </summary>
        /// <param name="filePath">需要提权的文件/目录</param>
        /// <returns></returns>
        public static bool GrantExecutePermission(string filePath)
        {
            if (OperatingSystem.IsWindows())
            {
                return true; // Windows系统不需要修改权限
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    var chmodProcess = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "/bin/chmod",
                            Arguments = $"+x \"{filePath}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    };
                    chmodProcess.Start();
                    chmodProcess.WaitForExit();
                    return chmodProcess.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    // 记录日志或处理异常
                    Console.WriteLine($"Error granting execute permission: {ex.Message}");
                    return false;
                }
            }
            return false;
        }
    }
}