using MSLX.Daemon.Utils;

namespace MSLX.Tests;

public class CrashRestartGuardTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void RecordCrash_FirstCrash_ReturnsOne()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        Assert.Equal(1, guard.RecordCrash(1, T0));
    }

    [Fact]
    public void RecordCrash_CrashesWithinWindow_CountAccumulates()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        for (int i = 1; i <= 5; i++)
        {
            Assert.Equal(i, guard.RecordCrash(1, T0.AddSeconds(i * 10)));
        }
    }

    [Fact]
    public void RecordCrash_ExceedingMaxCount_ReturnsCountAboveThreshold()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        int attempts = 0;
        for (int i = 0; i < 6; i++)
        {
            attempts = guard.RecordCrash(1, T0.AddSeconds(i * 10));
        }
        // 第 6 次崩溃 > 阈值 5，调用方应熔断放弃重启
        Assert.Equal(6, attempts);
        Assert.True(attempts > guard.MaxCount);
    }

    [Fact]
    public void RecordCrash_AtExactWindowBoundary_OldRecordIsKept()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        guard.RecordCrash(1, T0);
        // 恰好 300 秒时旧记录仍在窗口内（清理条件是 t < now - window）
        Assert.Equal(2, guard.RecordCrash(1, T0.AddSeconds(300)));
    }

    [Fact]
    public void RecordCrash_AfterWindowExpires_CountRestarts()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        guard.RecordCrash(1, T0);
        guard.RecordCrash(1, T0.AddSeconds(1));
        // 302 秒后前两次记录都已过期（窗口边界为"恰好 300 秒时仍保留"）
        Assert.Equal(1, guard.RecordCrash(1, T0.AddSeconds(302)));
    }

    [Fact]
    public void RecordCrash_DifferentInstances_AreTrackedIndependently()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        guard.RecordCrash(1, T0);
        guard.RecordCrash(1, T0.AddSeconds(1));

        Assert.Equal(1, guard.RecordCrash(2, T0.AddSeconds(2)));
        Assert.Equal(3, guard.RecordCrash(1, T0.AddSeconds(3)));
    }

    [Fact]
    public void Reset_ClearsInstanceHistory()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        guard.RecordCrash(1, T0);
        guard.RecordCrash(1, T0.AddSeconds(1));

        guard.Reset(1);

        Assert.Equal(1, guard.RecordCrash(1, T0.AddSeconds(2)));
    }

    [Fact]
    public void Reset_DoesNotAffectOtherInstances()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 5);
        guard.RecordCrash(1, T0);
        guard.RecordCrash(2, T0);

        guard.Reset(1);

        Assert.Equal(2, guard.RecordCrash(2, T0.AddSeconds(1)));
    }

    [Fact]
    public void RecordCrash_ConcurrentCalls_DoNotThrowAndCountAll()
    {
        var guard = new CrashRestartGuard(windowSeconds: 300, maxCount: 1000);
        Parallel.For(0, 100, _ => guard.RecordCrash(1, T0));
        Assert.Equal(101, guard.RecordCrash(1, T0));
    }
}
