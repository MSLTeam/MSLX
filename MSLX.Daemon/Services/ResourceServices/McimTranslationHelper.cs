using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MSLX.Daemon.Services.ResourceServices
{
    public static class McimTranslationHelper
    {
        public class ModrinthTranslation
        {
            public string project_id { get; set; }
            public string translated { get; set; }
        }

        public class CurseForgeTranslation
        {
            public int modid { get; set; }
            public string translated { get; set; }
        }

        public static async Task<Dictionary<string, string>> TranslateModrinthBatchAsync(HttpClient httpClient, IEnumerable<string> projectIds)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var body = new { project_ids = projectIds };
                var json = JsonSerializer.Serialize(body);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://mod.mcimirror.top/translate/modrinth");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await httpClient.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var list = JsonSerializer.Deserialize<List<ModrinthTranslation>>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            if (!string.IsNullOrEmpty(item.translated))
                            {
                                result[item.project_id] = item.translated;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McimTranslationHelper] Modrinth batch translation failed: {ex.Message}");
            }
            return result;
        }

        public static async Task<string> TranslateModrinthSingleAsync(HttpClient httpClient, string projectId)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await httpClient.GetAsync($"https://mod.mcimirror.top/translate/modrinth/{projectId}", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var trans = JsonSerializer.Deserialize<ModrinthTranslation>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return trans?.translated;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McimTranslationHelper] Modrinth single translation failed: {ex.Message}");
            }
            return null;
        }

        public static async Task<Dictionary<int, string>> TranslateCurseForgeBatchAsync(HttpClient httpClient, IEnumerable<int> modIds)
        {
            var result = new Dictionary<int, string>();
            try
            {
                var body = new { modids = modIds };
                var json = JsonSerializer.Serialize(body);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://mod.mcimirror.top/translate/curseforge");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await httpClient.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var list = JsonSerializer.Deserialize<List<CurseForgeTranslation>>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            if (!string.IsNullOrEmpty(item.translated))
                            {
                                result[item.modid] = item.translated;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McimTranslationHelper] CurseForge batch translation failed: {ex.Message}");
            }
            return result;
        }

        public static async Task<string> TranslateCurseForgeSingleAsync(HttpClient httpClient, int modId)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await httpClient.GetAsync($"https://mod.mcimirror.top/translate/curseforge/{modId}", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var trans = JsonSerializer.Deserialize<CurseForgeTranslation>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return trans?.translated;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McimTranslationHelper] CurseForge single translation failed: {ex.Message}");
            }
            return null;
        }
    }
}
