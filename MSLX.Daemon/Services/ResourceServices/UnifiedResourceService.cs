using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Resources;

namespace MSLX.Daemon.Services.ResourceServices
{
    public class UnifiedResourceService : IUnifiedResourceService
    {
        private readonly IEnumerable<IResourceProvider> _providers;
        private readonly ModDictionaryService _modDictionary;

        public UnifiedResourceService(IEnumerable<IResourceProvider> providers, ModDictionaryService modDictionary)
        {
            _providers = providers;
            _modDictionary = modDictionary;
        }

        public async Task<ResourceSearchResult> SearchAsync(ResourceSearchFilter filter)
        {
            var rawQuery = filter.Query?.Trim();
            string translatedQuery = null;

            // 中译英（本地词库）
            if (!string.IsNullOrWhiteSpace(filter.Query))
            {
                translatedQuery = _modDictionary.TranslateChineseQueryToEnglish(filter.Query);
                filter.Query = translatedQuery;
            }
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
                    UseMirror = filter.UseMirror,
                    Limit = subLimit + (i < remainderLimit ? 1 : 0),
                    Offset = subOffset + (i < remainderOffset ? 1 : 0)
                };
                tasks.Add(SafeSearchAsync(provider, subFilter));
            }
            
            var results = await Task.WhenAll(tasks);
            var merged = new List<Resource>();
            if (results.Length > 0)
            {
                int maxCount = results.Max(r => r.Items.Count());
                for (int i = 0; i < maxCount; i++)
                {
                    foreach (var result in results)
                    {
                        var list = result.Items as List<Resource> ?? result.Items.ToList();
                        if (i < list.Count)
                        {
                            merged.Add(list[i]);
                        }
                    }
                }
            }
            long totalCount = results.Sum(r => r.TotalCount);

            if (!string.IsNullOrWhiteSpace(rawQuery))
            {
                merged = merged
                    .Select(item =>
                    {
                        var origName = item.Name;
                        var cnName = _modDictionary.GetChineseName(origName);
                        var displayName = !string.IsNullOrEmpty(cnName) ? cnName : origName;
                        int score = CalculateRelevanceScore(displayName, origName, rawQuery, translatedQuery);
                        item.Name = displayName;
                        return new { Item = item, Score = score };
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Item.DownloadCount)
                    .Select(x => x.Item)
                    .ToList();
            }
            else
            {
                foreach (var item in merged)
                {
                    var cnName = _modDictionary.GetChineseName(item.Name);
                    if (!string.IsNullOrEmpty(cnName))
                    {
                        item.Name = cnName;
                    }
                }
            }

            return new ResourceSearchResult
            {
                Items = merged,
                TotalCount = totalCount
            };
        }

        private static int CalculateRelevanceScore(string displayName, string originalName, string rawQuery, string translatedQuery)
        {
            if (string.IsNullOrWhiteSpace(rawQuery)) return 0;

            int score = 0;
            var queries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rawQuery.Trim() };
            if (!string.IsNullOrWhiteSpace(translatedQuery))
            {
                queries.Add(translatedQuery.Trim());
            }

            var cleanDisplay = Regex.Replace(displayName ?? "", @"^\[.*?\]\s*", "").Trim();
            var cleanOriginal = Regex.Replace(originalName ?? "", @"^\[.*?\]\s*", "").Trim();

            foreach (var q in queries)
            {
                if (string.Equals(displayName, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(originalName, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cleanDisplay, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cleanOriginal, q, StringComparison.OrdinalIgnoreCase))
                {
                    score = Math.Max(score, 100);
                }
                else if ((displayName != null && displayName.StartsWith(q, StringComparison.OrdinalIgnoreCase)) ||
                         (originalName != null && originalName.StartsWith(q, StringComparison.OrdinalIgnoreCase)) ||
                         cleanDisplay.StartsWith(q, StringComparison.OrdinalIgnoreCase) ||
                         cleanOriginal.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                {
                    score = Math.Max(score, 60);
                }
                else if ((displayName != null && displayName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                         (originalName != null && originalName.Contains(q, StringComparison.OrdinalIgnoreCase)))
                {
                    score = Math.Max(score, 30);
                }
            }

            return score;
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

        public async Task<Resource> GetResourceAsync(string id, ResourceProviderType providerType, bool useMirror = true)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
            if (provider == null)
            {
                throw new NotSupportedException($"提供商 {providerType} 不存在.");
            }
            return await provider.GetResourceAsync(id, useMirror);
        }

        public async Task<IEnumerable<ResourceVersion>> GetVersionsAsync(string id, ResourceProviderType providerType, string gameVersion = null, string loader = null, bool useMirror = true)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
            if (provider == null)
            {
                throw new NotSupportedException($"提供商 {providerType} 不存在.");
            }
            return await provider.GetVersionsAsync(id, gameVersion, loader, useMirror);
        }
    }
}
