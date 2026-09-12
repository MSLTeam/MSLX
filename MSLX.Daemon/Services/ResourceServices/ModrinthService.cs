using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Resources;

namespace MSLX.Daemon.Services.ResourceServices
{
    public class ModrinthService : IResourceProvider
    {
        public ResourceProviderType ProviderType => ResourceProviderType.Modrinth;

        private readonly HttpClient _httpClient;

        // Modrinth 类型映射
        private static string GetProjectTypeFacet(ResourceType type)
        {
            return type switch
            {
                ResourceType.Mod => "mod",
                ResourceType.ResourcePack => "resourcepack",
                ResourceType.DataPack => "datapack",
                ResourceType.Shader => "shader",
                ResourceType.Modpack => "modpack",
                ResourceType.Plugin => "plugin",
                _ => "mod"
            };
        }

        public ModrinthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ResourceSearchResult> SearchAsync(ResourceSearchFilter filter)
        {
            var baseUrl = filter.UseMirror ? "https://mod.mcimirror.top/modrinth/v2/search?" : "https://api.modrinth.com/v2/search?";
            var urlBuilder = new StringBuilder(baseUrl);
            
            if (!string.IsNullOrEmpty(filter.Query))
            {
                urlBuilder.Append($"query={Uri.EscapeDataString(filter.Query)}&");
            }

            // Facets
            var facets = new List<string>();
            
            // Type facet
            if (filter.Type.HasValue)
            {
                facets.Add($"[\"project_type:{GetProjectTypeFacet(filter.Type.Value)}\"]");
            }
            
            // Version facet
            if (!string.IsNullOrEmpty(filter.GameVersion))
            {
                facets.Add($"[\"versions:{filter.GameVersion}\"]");
            }
            
            // Loaders
            if (filter.GameLoaders != null && filter.GameLoaders.Count > 0)
            {
                foreach (var loader in filter.GameLoaders)
                {
                    facets.Add($"[\"categories:{loader.ToLower()}\"]");
                }
            }

            if (facets.Count > 0)
            {
                urlBuilder.Append($"facets=[{string.Join(",", facets)}]&");
            }

            if (string.IsNullOrEmpty(filter.Query))
            {
                // 如果为空字符串查询，默认根据热度排序
                urlBuilder.Append("index=downloads&");
            }
            
            urlBuilder.Append($"offset={filter.Offset}&limit={filter.Limit}");

            using var request = new HttpRequestMessage(HttpMethod.Get, urlBuilder.ToString());
            request.Headers.Add("User-Agent", "MSLTeam/MSLX");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var results = new List<Resource>();
            var projectIds = new List<string>();

            if (doc.RootElement.TryGetProperty("hits", out var hitsArray))
            {
                foreach (var element in hitsArray.EnumerateArray())
                {
                    var id = element.GetProperty("project_id").GetString();
                    projectIds.Add(id);

                    results.Add(new Resource
                    {
                        Id = id,
                        Name = element.GetProperty("title").GetString(),
                        Summary = element.GetProperty("description").GetString(),
                        IconUrl = element.TryGetProperty("icon_url", out var iconProp) && iconProp.ValueKind == JsonValueKind.String ? iconProp.GetString() : null,
                        Author = element.TryGetProperty("author", out var authProp) && authProp.ValueKind == JsonValueKind.String ? authProp.GetString() : "Unknown",
                        Provider = ResourceProviderType.Modrinth,
                        DownloadCount = element.GetProperty("downloads").GetInt64(),
                        UpdatedAt = element.GetProperty("date_modified").GetDateTime()
                    });
                }
            }

            long totalHits = 0;
            if (doc.RootElement.TryGetProperty("total_hits", out var totalProp))
            {
                totalHits = totalProp.GetInt64();
            }

            if (filter.UseMirror && results.Count > 0)
            {
                var translations = await McimTranslationHelper.TranslateModrinthBatchAsync(_httpClient, projectIds);
                foreach (var res in results)
                {
                    if (translations.TryGetValue(res.Id, out var translatedStr))
                    {
                        res.TranslatedSummary = translatedStr;
                        res.Summary = translatedStr; // Replace default summary with translated one for UI mapping
                    }
                }
            }

            return new ResourceSearchResult
            {
                Items = results,
                TotalCount = totalHits
            };
        }

        public async Task<Resource> GetResourceAsync(string id, bool useMirror = true)
        {
            var baseUrl = useMirror ? $"https://mod.mcimirror.top/modrinth/v2/project/{id}" : $"https://api.modrinth.com/v2/project/{id}";
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            request.Headers.Add("User-Agent", "MSLTeam/MSLX");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement;

            DateTime updatedAt = DateTime.MinValue;
            if (element.TryGetProperty("updated", out var updatedProp) && updatedProp.ValueKind == JsonValueKind.String)
            {
                updatedAt = updatedProp.GetDateTime();
            }

            var resource = new Resource
            {
                Id = element.GetProperty("id").GetString(),
                Name = element.GetProperty("title").GetString(),
                Summary = element.GetProperty("description").GetString(),
                Description = element.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String ? bodyProp.GetString() : "",
                IconUrl = element.TryGetProperty("icon_url", out var iconProp) && iconProp.ValueKind == JsonValueKind.String ? iconProp.GetString() : null,
                Author = "Unknown",
                Provider = ResourceProviderType.Modrinth,
                DownloadCount = element.GetProperty("downloads").GetInt64(),
                UpdatedAt = updatedAt
            };

            if (useMirror)
            {
                var translatedStr = await McimTranslationHelper.TranslateModrinthSingleAsync(_httpClient, resource.Id);
                if (!string.IsNullOrEmpty(translatedStr))
                {
                    resource.TranslatedSummary = translatedStr;
                    resource.Summary = translatedStr;
                }
            }

            return resource;
        }

        public async Task<IEnumerable<ResourceVersion>> GetVersionsAsync(string id, string gameVersion = null, string loader = null, bool useMirror = true)
        {
            var baseUrl = useMirror ? $"https://mod.mcimirror.top/modrinth/v2/project/{id}/version?" : $"https://api.modrinth.com/v2/project/{id}/version?";
            var urlBuilder = new StringBuilder(baseUrl);
            
            if (!string.IsNullOrEmpty(gameVersion))
            {
                urlBuilder.Append($"game_versions=[\"{Uri.EscapeDataString(gameVersion)}\"]&");
            }
            if (!string.IsNullOrEmpty(loader))
            {
                urlBuilder.Append($"loaders=[\"{Uri.EscapeDataString(loader.ToLower())}\"]&");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, urlBuilder.ToString());
            request.Headers.Add("User-Agent", "MSLTeam/MSLX");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var results = new List<ResourceVersion>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var files = element.GetProperty("files");
                foreach (var file in files.EnumerateArray())
                {
                    string filename = file.GetProperty("filename").GetString();
                    int env = 0; // Client
                    if (filename != null && filename.Contains("server", StringComparison.OrdinalIgnoreCase))
                    {
                        env = 1; // Server
                    }

                    var dependencies = new List<ResourceDependency>();
                    if (element.TryGetProperty("dependencies", out var depsArray))
                    {
                        foreach (var dep in depsArray.EnumerateArray())
                        {
                            var depTypeStr = dep.GetProperty("dependency_type").GetString();
                            DependencyType depType = DependencyType.Required;
                            if (depTypeStr == "optional") depType = DependencyType.Optional;
                            else if (depTypeStr == "incompatible") depType = DependencyType.Incompatible;
                            else if (depTypeStr == "embedded") depType = DependencyType.Embedded;

                            dependencies.Add(new ResourceDependency
                            {
                                ProjectId = dep.TryGetProperty("project_id", out var pid) && pid.ValueKind == JsonValueKind.String ? pid.GetString() : null,
                                VersionId = dep.TryGetProperty("version_id", out var vid) && vid.ValueKind == JsonValueKind.String ? vid.GetString() : null,
                                Type = depType,
                                Provider = ResourceProviderType.Modrinth
                            });
                        }
                    }

                    results.Add(new ResourceVersion
                    {
                        Id = element.GetProperty("id").GetString(),
                        Name = element.GetProperty("name").GetString(),
                        VersionNumber = element.GetProperty("version_number").GetString(),
                        DownloadUrl = file.GetProperty("url").GetString(),
                        Filename = filename,
                        FileSizeBytes = file.GetProperty("size").GetInt64(),
                        Environment = env,
                        GameVersions = element.GetProperty("game_versions").EnumerateArray().Select(x => x.GetString()).ToList(),
                        Loaders = element.GetProperty("loaders").EnumerateArray().Select(x => x.GetString()).ToList(),
                        Dependencies = dependencies
                    });
                }
            }
            return results;
        }
    }
}
