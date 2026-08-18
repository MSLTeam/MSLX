using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Resources;

namespace MSLX.Daemon.Services.ResourceServices
{
    public class CurseForgeService : IResourceProvider
    {
        public ResourceProviderType ProviderType => ResourceProviderType.CurseForge;

        private readonly HttpClient _httpClient;
        private string _cachedToken;
        private DateTime _tokenFetchTime;

        // CurseForge Class ID 静态映射
        private static readonly Dictionary<ResourceType, int> ClassIdMap = new Dictionary<ResourceType, int>
        {
            { ResourceType.Mod, 6 },
            { ResourceType.Modpack, 4471 },
            { ResourceType.ResourcePack, 12 },
            { ResourceType.Shader, 6552 },
        };

        public CurseForgeService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        private async Task<string> GetApiKeyAsync()
        {
            // 缓存 api token
            if (!string.IsNullOrEmpty(_cachedToken) && (DateTime.Now - _tokenFetchTime).TotalHours < 12)
            {
                return _cachedToken;
            }

            try
            {
                var response = await _httpClient.GetStringAsync("https://api.mslmc.cn/v4/software/cf_token");
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String)
                {
                    string base64Token = dataElement.GetString();
                    byte[] base64EncodedBytes = Convert.FromBase64String(base64Token.Trim());
                    _cachedToken = Encoding.UTF8.GetString(base64EncodedBytes);
                    _tokenFetchTime = DateTime.Now;
                    return _cachedToken;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CurseForgeService] 获取 API Token 失败: {ex.Message}");
            }

            return _cachedToken;
        }

        public async Task<ResourceSearchResult> SearchAsync(ResourceSearchFilter filter)
        {
            string token = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(token)) return new ResourceSearchResult { Items = Enumerable.Empty<Resource>(), TotalCount = 0 };

            if (filter.Type == ResourceType.Plugin || filter.Type == ResourceType.DataPack)
            {
                return new ResourceSearchResult { Items = Enumerable.Empty<Resource>(), TotalCount = 0 };
            }

            // 构建 CurseForge 查询 URL 
            var urlBuilder = new StringBuilder("https://api.curseforge.com/v1/mods/search?gameId=432"); // 432 = Minecraft
            
            if (filter.Type.HasValue && ClassIdMap.TryGetValue(filter.Type.Value, out int classId))
            {
                urlBuilder.Append($"&classId={classId}");
            }

            if (!string.IsNullOrEmpty(filter.Query))
            {
                urlBuilder.Append($"&searchFilter={Uri.EscapeDataString(filter.Query)}");
            }
            else
            {
                // 如果查询为空，可以通过排序参数获取默认热门列表
                urlBuilder.Append("&sortField=2&sortOrder=desc"); // 2 = Popularity
            }

            if (!string.IsNullOrEmpty(filter.GameVersion))
            {
                urlBuilder.Append($"&gameVersion={Uri.EscapeDataString(filter.GameVersion)}");
            }

            urlBuilder.Append($"&index={filter.Offset}&pageSize={filter.Limit}");

            using var request = new HttpRequestMessage(HttpMethod.Get, urlBuilder.ToString());
            request.Headers.Add("x-api-key", token);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // 解析 JSON
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var results = new List<Resource>();
            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var element in dataArray.EnumerateArray())
                {
                    string authorName = "Unknown";
                    if (element.TryGetProperty("authors", out var authorsArray) && authorsArray.ValueKind == JsonValueKind.Array && authorsArray.GetArrayLength() > 0)
                    {
                        authorName = authorsArray[0].GetProperty("name").GetString() ?? "Unknown";
                    }

                    results.Add(new Resource
                    {
                        Id = element.GetProperty("id").GetInt32().ToString(),
                        Name = element.GetProperty("name").GetString(),
                        Summary = element.GetProperty("summary").GetString(),
                        IconUrl = element.TryGetProperty("logo", out var logoProp) && logoProp.ValueKind == JsonValueKind.Object && logoProp.TryGetProperty("thumbnailUrl", out var thumbProp) ? thumbProp.GetString() : null,
                        Author = authorName,
                        Provider = ResourceProviderType.CurseForge,
                        DownloadCount = element.GetProperty("downloadCount").GetInt64(),
                        UpdatedAt = element.GetProperty("dateModified").GetDateTime()
                    });
                }
            }

            long totalCount = 0;
            if (doc.RootElement.TryGetProperty("pagination", out var paginationProp) && paginationProp.TryGetProperty("totalCount", out var totalCountProp))
            {
                totalCount = totalCountProp.GetInt64();
            }

            return new ResourceSearchResult
            {
                Items = results,
                TotalCount = totalCount
            };
        }

        public async Task<Resource> GetResourceAsync(string id)
        {
            string token = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(token)) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.curseforge.com/v1/mods/{id}");
            request.Headers.Add("x-api-key", token);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var element))
            {
                string authorName = "Unknown";
                if (element.TryGetProperty("authors", out var authorsArray) && authorsArray.ValueKind == JsonValueKind.Array && authorsArray.GetArrayLength() > 0)
                {
                    authorName = authorsArray[0].GetProperty("name").GetString() ?? "Unknown";
                }

                // Fetch description
                string description = "";
                using var descRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.curseforge.com/v1/mods/{id}/description");
                descRequest.Headers.Add("x-api-key", token);
                descRequest.Headers.Add("Accept", "application/json");
                var descResponse = await _httpClient.SendAsync(descRequest);
                if (descResponse.IsSuccessStatusCode)
                {
                    var descJson = await descResponse.Content.ReadAsStringAsync();
                    using var descDoc = JsonDocument.Parse(descJson);
                    if (descDoc.RootElement.TryGetProperty("data", out var descData) && descData.ValueKind == JsonValueKind.String)
                    {
                        description = descData.GetString();
                    }
                }

                return new Resource
                {
                    Id = element.GetProperty("id").GetInt32().ToString(),
                    Name = element.GetProperty("name").GetString(),
                    Summary = element.GetProperty("summary").GetString(),
                    Description = description,
                    IconUrl = element.TryGetProperty("logo", out var logoProp) && logoProp.ValueKind == JsonValueKind.Object && logoProp.TryGetProperty("thumbnailUrl", out var thumbProp) ? thumbProp.GetString() : null,
                    Author = authorName,
                    Provider = ResourceProviderType.CurseForge,
                    DownloadCount = element.GetProperty("downloadCount").GetInt64(),
                    UpdatedAt = element.GetProperty("dateModified").GetDateTime()
                };
            }

            return null;
        }

        public async Task<IEnumerable<ResourceVersion>> GetVersionsAsync(string id, string gameVersion = null, string loader = null)
        {
            string token = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(token)) return Enumerable.Empty<ResourceVersion>();

            var urlBuilder = new StringBuilder($"https://api.curseforge.com/v1/mods/{id}/files?pageSize=200&");
            
            if (!string.IsNullOrEmpty(gameVersion))
            {
                urlBuilder.Append($"gameVersion={Uri.EscapeDataString(gameVersion)}&");
            }
            
            // CurseForge ModLoaderTypes:
            // 1 = Forge, 2 = Cauldron, 3 = LiteLoader, 4 = Fabric, 5 = Quilt, 6 = NeoForge
            if (!string.IsNullOrEmpty(loader))
            {
                int? modLoaderType = loader.ToLower() switch
                {
                    "forge" => 1,
                    "fabric" => 4,
                    "quilt" => 5,
                    "neoforge" => 6,
                    _ => null
                };
                if (modLoaderType.HasValue)
                {
                    urlBuilder.Append($"modLoaderType={modLoaderType.Value}&");
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, urlBuilder.ToString());
            request.Headers.Add("x-api-key", token);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var results = new List<ResourceVersion>();
            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                var tempDict = new Dictionary<int, ResourceVersion>();
                var serverPackToClient = new Dictionary<int, int>();

                foreach (var element in dataArray.EnumerateArray())
                {
                    int fileId = element.GetProperty("id").GetInt32();
                    string fileName = element.GetProperty("fileName").GetString();
                    
                    var downloadUrlProp = element.GetProperty("downloadUrl");
                    string downloadUrl = downloadUrlProp.ValueKind == JsonValueKind.String 
                        ? downloadUrlProp.GetString() 
                        : $"https://edge.forgecdn.net/files/{fileId / 1000}/{(fileId % 1000):D3}/{fileName}";

                    var gvs = new List<string>();
                    var lds = new List<string>();
                    if (element.TryGetProperty("gameVersions", out var gvArray))
                    {
                        foreach (var gv in gvArray.EnumerateArray())
                        {
                            var val = gv.GetString();
                            if (string.IsNullOrEmpty(val)) continue;

                            if (val.Equals("Forge", StringComparison.OrdinalIgnoreCase) || 
                                val.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
                                val.Equals("Quilt", StringComparison.OrdinalIgnoreCase) ||
                                val.Equals("NeoForge", StringComparison.OrdinalIgnoreCase) ||
                                val.Equals("LiteLoader", StringComparison.OrdinalIgnoreCase))
                            {
                                lds.Add(val);
                            }
                            else if (val.StartsWith("1.") || val.StartsWith("b1.") || val.StartsWith("a1.") || (val.Contains("w") && val.Length <= 7))
                            {
                                gvs.Add(val);
                            }
                        }
                    }

                    bool isServer = false;
                    if (element.TryGetProperty("isServerPack", out var isServerProp) && isServerProp.ValueKind == JsonValueKind.True)
                    {
                        isServer = true;
                    }
                    if (fileName != null && fileName.Contains("server", StringComparison.OrdinalIgnoreCase))
                    {
                        isServer = true;
                    }

                    if (element.TryGetProperty("serverPackFileId", out var spIdProp) && spIdProp.ValueKind == JsonValueKind.Number)
                    {
                        int spId = spIdProp.GetInt32();
                        if (spId > 0)
                        {
                            serverPackToClient[spId] = fileId;
                        }
                    }

                    var dependencies = new List<ResourceDependency>();
                    if (element.TryGetProperty("dependencies", out var depsArray))
                    {
                        foreach (var dep in depsArray.EnumerateArray())
                        {
                            var modId = dep.GetProperty("modId").GetInt32().ToString();
                            var relType = dep.GetProperty("relationType").GetInt32();
                            DependencyType depType = DependencyType.Required;
                            if (relType == 2) depType = DependencyType.Optional;
                            else if (relType == 5) depType = DependencyType.Incompatible;
                            else if (relType == 1 || relType == 6) depType = DependencyType.Embedded;

                            dependencies.Add(new ResourceDependency
                            {
                                ProjectId = modId,
                                Type = depType,
                                Provider = ResourceProviderType.CurseForge
                            });
                        }
                    }

                    var rv = new ResourceVersion
                    {
                        Id = fileId.ToString(),
                        Name = element.GetProperty("displayName").GetString(),
                        VersionNumber = element.GetProperty("displayName").GetString(),
                        DownloadUrl = downloadUrl,
                        Filename = fileName,
                        FileSizeBytes = element.GetProperty("fileLength").GetInt64(),
                        GameVersions = gvs,
                        Loaders = lds,
                        Environment = isServer ? 1 : 0,
                        Dependencies = dependencies
                    };

                    tempDict[fileId] = rv;
                    results.Add(rv);
                }

                if (serverPackToClient.Count > 0)
                {
                    // 从额外资源获取服务端包
                    var fileIdsJson = JsonSerializer.Serialize(new { fileIds = serverPackToClient.Keys });
                    using var postReq = new HttpRequestMessage(HttpMethod.Post, "https://api.curseforge.com/v1/mods/files");
                    postReq.Headers.Add("x-api-key", token);
                    postReq.Headers.Add("Accept", "application/json");
                    postReq.Content = new StringContent(fileIdsJson, Encoding.UTF8, "application/json");

                    var postRes = await _httpClient.SendAsync(postReq);
                    if (postRes.IsSuccessStatusCode)
                    {
                        var postJson = await postRes.Content.ReadAsStringAsync();
                        using var postDoc = JsonDocument.Parse(postJson);
                        if (postDoc.RootElement.TryGetProperty("data", out var postDataArray))
                        {
                            foreach (var element in postDataArray.EnumerateArray())
                            {
                                int fileId = element.GetProperty("id").GetInt32();
                                string fileName = element.GetProperty("fileName").GetString();

                                var downloadUrlProp = element.GetProperty("downloadUrl");
                                string downloadUrl = downloadUrlProp.ValueKind == JsonValueKind.String 
                                    ? downloadUrlProp.GetString() 
                                    : $"https://edge.forgecdn.net/files/{fileId / 1000}/{(fileId % 1000):D3}/{fileName}";

                                var gvs = new List<string>();
                                var lds = new List<string>();
                                if (element.TryGetProperty("gameVersions", out var gvArray))
                                {
                                    foreach (var gv in gvArray.EnumerateArray())
                                    {
                                        var val = gv.GetString();
                                        if (string.IsNullOrEmpty(val)) continue;

                                        if (val.Equals("Forge", StringComparison.OrdinalIgnoreCase) || 
                                            val.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
                                            val.Equals("Quilt", StringComparison.OrdinalIgnoreCase) ||
                                            val.Equals("NeoForge", StringComparison.OrdinalIgnoreCase) ||
                                            val.Equals("LiteLoader", StringComparison.OrdinalIgnoreCase))
                                        {
                                            lds.Add(val);
                                        }
                                        else if (val.StartsWith("1.") || val.StartsWith("b1.") || val.StartsWith("a1.") || (val.Contains("w") && val.Length <= 7))
                                        {
                                            gvs.Add(val);
                                        }
                                    }
                                }

                                var dependencies = new List<ResourceDependency>();
                                if (element.TryGetProperty("dependencies", out var depsArray))
                                {
                                    foreach (var dep in depsArray.EnumerateArray())
                                    {
                                        var modId = dep.GetProperty("modId").GetInt32().ToString();
                                        var relType = dep.GetProperty("relationType").GetInt32();
                                        DependencyType depType = DependencyType.Required;
                                        if (relType == 2) depType = DependencyType.Optional;
                                        else if (relType == 5) depType = DependencyType.Incompatible;
                                        else if (relType == 1 || relType == 6) depType = DependencyType.Embedded;

                                        dependencies.Add(new ResourceDependency
                                        {
                                            ProjectId = modId,
                                            Type = depType,
                                            Provider = ResourceProviderType.CurseForge
                                        });
                                    }
                                }

                                var rv = new ResourceVersion
                                {
                                    Id = fileId.ToString(),
                                    Name = element.GetProperty("displayName").GetString(),
                                    VersionNumber = element.GetProperty("displayName").GetString(),
                                    DownloadUrl = downloadUrl,
                                    Filename = fileName,
                                    FileSizeBytes = element.GetProperty("fileLength").GetInt64(),
                                    GameVersions = gvs,
                                    Loaders = lds,
                                    Environment = 1,
                                    Dependencies = dependencies
                                };

                                results.Add(rv);
                            }
                        }
                    }
                }

                // 链接服务端包的元数据
                foreach (var rv in results)
                {
                    int rvId = int.Parse(rv.Id);
                    if (serverPackToClient.TryGetValue(rvId, out int clientId))
                    {
                        rv.Environment = 1;
                        if (tempDict.TryGetValue(clientId, out var clientRv))
                        {
                            if (rv.Loaders.Count == 0 && clientRv.Loaders.Count > 0)
                            {
                                rv.Loaders = new List<string>(clientRv.Loaders);
                            }
                            if (rv.GameVersions.Count == 0 && clientRv.GameVersions.Count > 0)
                            {
                                rv.GameVersions = new List<string>(clientRv.GameVersions);
                            }
                        }
                    }
                }
            }
            return results;
        }
    }
}
