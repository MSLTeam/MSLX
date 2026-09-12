using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Porta.Pty;
using Xunit;

namespace MSLX.Tests;

public class PtyIntegrationTests
{
    [Fact]
    public async Task PtyProvider_CanSpawnAndReadOutput_CrossPlatform()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string app = isWindows ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh";
        string[] args = isWindows ? ["/c", "echo", "MSLX_PTY_OK"] : ["-c", "echo MSLX_PTY_OK"];

        var options = new PtyOptions
        {
            Name = "TestPty",
            Cols = 80,
            Rows = 24,
            Cwd = Directory.GetCurrentDirectory(),
            App = app,
            CommandLine = args
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using IPtyConnection pty = await PtyProvider.SpawnAsync(options, cts.Token);

        Assert.True(pty.Pid > 0);

        byte[] buffer = new byte[1024];
        var sb = new StringBuilder();

        while (!cts.Token.IsCancellationRequested)
        {
            int read = await pty.ReaderStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (read <= 0) break;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (sb.ToString().Contains("MSLX_PTY_OK"))
                break;
        }

        Assert.Contains("MSLX_PTY_OK", sb.ToString());
    }

    [Fact]
    public async Task PtyProvider_CanResize()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string app = isWindows ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh";
        string[] args = isWindows ? ["/c", "exit", "0"] : ["-c", "exit 0"];

        var options = new PtyOptions
        {
            Name = "TestResize",
            Cols = 80,
            Rows = 24,
            Cwd = Directory.GetCurrentDirectory(),
            App = app,
            CommandLine = args
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using IPtyConnection pty = await PtyProvider.SpawnAsync(options, cts.Token);

        // Resize should not throw
        pty.Resize(120, 40);
        pty.Resize(80, 24);
    }
}
