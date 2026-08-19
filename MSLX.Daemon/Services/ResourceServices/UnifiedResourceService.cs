using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Resources;

namespace MSLX.Daemon.Services.ResourceServices
{
    public class UnifiedResourceService : IUnifiedResourceService
    {
        private readonly IEnumerable<IResourceProvider> _providers;

        public UnifiedResourceService(IEnumerable<IResourceProvider> providers)
        {
            _providers = providers;
        }

        public async Task<ResourceSearchResult> SearchAsync(ResourceSearchFilter filter)
        {
            var activeProviders = _providers.Where(p => 
            {
                if (filter.Provider != null && filter.Provider != p.ProviderType) return false;
                if (p.ProviderType == ResourceProviderType.CurseForge && (filter.Type == ResourceType.Plugin || filter.Type == ResourceType.DataPack)) return false;
                return true;
            }).ToList();

            if (activeProviders.Count == 0) return new ResourceSearchResult { Items = Enumerable.Empty<Resource>(), TotalCount = 0 };

            int subLimit = filter.Limit / activeProviders.Count;
            int subOffset = filter.Offset / activeProviders.Count;
            int remainderLimit = filter.Limit % activeProviders.Count;
            int remainderOffset = filter.Offset % activeProviders.Count;

            var tasks = new List<Task<ResourceSearchResult>>();

            for (int i = 0; i < activeProviders.Count; i++)
            {
                var provider = activeProviders[i];
                var subFilter = new ResourceSearchFilter
                {
                    Query = filter.Query,
                    Type = filter.Type,
                    GameVersion = filter.GameVersion,
                    GameLoaders = filter.GameLoaders,
                    PluginLoaders = filter.PluginLoaders,
                    Category = filter.Category,
                    Provider = provider.ProviderType,
                    Limit = subLimit + (i < remainderLimit ? 1 : 0),
                    Offset = subOffset + (i < remainderOffset ? 1 : 0)
                };
                tasks.Add(SafeSearchAsync(provider, subFilter));
            }
            
            var results = await Task.WhenAll(tasks);
            var merged = results.SelectMany(r => r.Items).ToList();
            long totalCount = results.Sum(r => r.TotalCount);

            return new ResourceSearchResult
            {
                Items = merged.OrderBy(r => r.Name).ThenByDescending(r => r.DownloadCount).ToList(),
                TotalCount = totalCount
            };
        }

        private async Task<ResourceSearchResult> SafeSearchAsync(IResourceProvider provider, ResourceSearchFilter filter)
        {
            try
            {
                return await provider.SearchAsync(filter);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UnifiedResourceService] 资源提供商 {provider.ProviderType} 搜索失败: {ex.Message}");
                return new ResourceSearchResult { Items = Enumerable.Empty<Resource>(), TotalCount = 0 };
            }
        }

        public async Task<Resource> GetResourceAsync(string id, ResourceProviderType providerType)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
            if (provider == null)
            {
                throw new NotSupportedException($"提供商 {providerType} 不存在.");
            }
            return await provider.GetResourceAsync(id);
        }

        public async Task<IEnumerable<ResourceVersion>> GetVersionsAsync(string id, ResourceProviderType providerType, string gameVersion = null, string loader = null)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
            if (provider == null)
            {
                throw new NotSupportedException($"提供商 {providerType} 不存在.");
            }
            return await provider.GetVersionsAsync(id, gameVersion, loader);
        }
    }
}
