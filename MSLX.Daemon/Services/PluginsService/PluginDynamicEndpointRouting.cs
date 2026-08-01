using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using System.Collections;

namespace MSLX.Daemon.Services.PluginsService;

public class PluginCompositeEndpointDataSource : EndpointDataSource
{
    private readonly List<EndpointDataSource> _dataSources = new();
    private CancellationTokenSource _cts = new();

    public void AddDataSources(IEnumerable<EndpointDataSource> dataSources)
    {
        _dataSources.AddRange(dataSources);
        TriggerChange();
    }

    public void RemoveDataSources(IEnumerable<EndpointDataSource> dataSources)
    {
        foreach (var ds in dataSources)
        {
            _dataSources.Remove(ds);
        }
        TriggerChange();
    }

    private void TriggerChange()
    {
        var oldCts = _cts;
        _cts = new CancellationTokenSource();
        oldCts.Cancel();
    }

    public override IReadOnlyList<Endpoint> Endpoints => _dataSources.SelectMany(ds => ds.Endpoints).ToList();

    public override IChangeToken GetChangeToken()
    {
        return new CancellationChangeToken(_cts.Token);
    }
}

public class PluginEndpointRouteBuilderWrapper : IEndpointRouteBuilder
{
    private readonly IEndpointRouteBuilder _app;
    
    // 捕获插件注册的所有 DataSource
    public List<EndpointDataSource> CapturedDataSources { get; } = new();

    public PluginEndpointRouteBuilderWrapper(IEndpointRouteBuilder app)
    {
        _app = app;
    }

    public IServiceProvider ServiceProvider => _app.ServiceProvider;

    public ICollection<EndpointDataSource> DataSources => new HookedCollection(CapturedDataSources);

    public IApplicationBuilder CreateApplicationBuilder() => _app.CreateApplicationBuilder();

    private class HookedCollection : ICollection<EndpointDataSource>
    {
        private readonly List<EndpointDataSource> _inner;

        public HookedCollection(List<EndpointDataSource> inner)
        {
            _inner = inner;
        }

        public int Count => _inner.Count;
        public bool IsReadOnly => false;
        public void Add(EndpointDataSource item) => _inner.Add(item);
        public void Clear() => _inner.Clear();
        public bool Contains(EndpointDataSource item) => _inner.Contains(item);
        public void CopyTo(EndpointDataSource[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);
        public IEnumerator<EndpointDataSource> GetEnumerator() => _inner.GetEnumerator();
        public bool Remove(EndpointDataSource item) => _inner.Remove(item);
        IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();
    }
}
