using MSLX.Daemon.Services;
using MSLX.Daemon.Services.InstanceServices;

namespace MSLX.Tests;

public class InstanceStateStoreTests
{
    [Fact]
    public void Get_MissingInstance_ReturnsNull()
    {
        var store = new InstanceStateStore();
        Assert.Null(store.Get(42));
    }

    [Fact]
    public void Set_ThenGet_ReturnsSameContext()
    {
        var store = new InstanceStateStore();
        var context = new ServerContext();

        store.Set(1, context);

        Assert.Same(context, store.Get(1));
    }

    [Fact]
    public void Set_ExistingInstance_Overwrites()
    {
        var store = new InstanceStateStore();
        var first = new ServerContext();
        var second = new ServerContext();

        store.Set(1, first);
        store.Set(1, second);

        Assert.Same(second, store.Get(1));
    }

    [Fact]
    public void Remove_ExistingInstance_GetReturnsNull()
    {
        var store = new InstanceStateStore();
        store.Set(1, new ServerContext());

        store.Remove(1);

        Assert.Null(store.Get(1));
    }

    [Fact]
    public void Remove_MissingInstance_DoesNotThrow()
    {
        var store = new InstanceStateStore();
        store.Remove(999);
    }

    [Fact]
    public void HasAnyActive_ReflectsContents()
    {
        var store = new InstanceStateStore();
        Assert.False(store.HasAnyActive);

        store.Set(1, new ServerContext());
        Assert.True(store.HasAnyActive);

        store.Remove(1);
        Assert.False(store.HasAnyActive);
    }

    [Fact]
    public void ActiveEntries_EnumeratesAllInstances()
    {
        var store = new InstanceStateStore();
        store.Set(1, new ServerContext());
        store.Set(2, new ServerContext());
        store.Set(3, new ServerContext());

        var ids = store.ActiveEntries.Select(kvp => kvp.Key).OrderBy(x => x).ToList();

        Assert.Equal(new uint[] { 1, 2, 3 }, ids);
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var store = new InstanceStateStore();
        store.Set(1, new ServerContext());
        store.Set(2, new ServerContext());

        store.ClearAll();

        Assert.False(store.HasAnyActive);
        Assert.Null(store.Get(1));
    }

    [Fact]
    public void IsRestarting_UnmarkedInstance_ReturnsFalse()
    {
        var store = new InstanceStateStore();
        Assert.False(store.IsRestarting(1));
    }

    [Fact]
    public void MarkRestarting_ThenIsRestarting_ReturnsTrue()
    {
        var store = new InstanceStateStore();
        store.MarkRestarting(1);

        Assert.True(store.IsRestarting(1));
        Assert.False(store.IsRestarting(2));
    }

    [Fact]
    public void ClearRestarting_ResetsFlag()
    {
        var store = new InstanceStateStore();
        store.MarkRestarting(1);

        store.ClearRestarting(1);

        Assert.False(store.IsRestarting(1));
    }

    [Fact]
    public void TryGetPreferredTerminalSize_Missing_ReturnsFalse()
    {
        var store = new InstanceStateStore();
        Assert.False(store.TryGetPreferredTerminalSize(1, out _));
    }

    [Fact]
    public void SetPreferredTerminalSize_ThenTryGet_RoundTrips()
    {
        var store = new InstanceStateStore();
        store.SetPreferredTerminalSize(1, 120, 40);

        Assert.True(store.TryGetPreferredTerminalSize(1, out var size));
        Assert.Equal((120, 40), size);
    }

    [Fact]
    public void ConcurrentSetAndRemove_DoesNotThrow()
    {
        var store = new InstanceStateStore();
        Parallel.For(0, 200, i =>
        {
            uint id = (uint)(i % 10);
            store.Set(id, new ServerContext());
            store.MarkRestarting(id);
            store.SetPreferredTerminalSize(id, 80, 24);
            _ = store.Get(id);
            _ = store.IsRestarting(id);
            store.Remove(id);
            store.ClearRestarting(id);
        });
    }
}
