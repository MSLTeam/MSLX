using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MSLX.Daemon.Services.ResourceServices
{
    public class ModDictionaryService
    {
        private readonly Dictionary<string, string> _dict = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _reverseDict = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<ModDictionaryService> _logger;

        public ModDictionaryService(ILogger<ModDictionaryService> logger)
        {
            _logger = logger;
            LoadDictionary();
        }

        private void LoadDictionary()
        {
            try
            {
                var assembly = typeof(ModDictionaryService).Assembly;
                // 嵌入资源名格式：<默认命名空间>.<目录>.<文件名>
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("MSLX_ModDictionary.json", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    _logger.LogWarning("[ModDictionaryService] Embedded dictionary resource not found.");
                    return;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                var rawDict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (rawDict != null)
                {
                    foreach (var kvp in rawDict)
                    {
                        _dict[kvp.Key] = kvp.Value;
                        if (!string.IsNullOrWhiteSpace(kvp.Value) && !_reverseDict.ContainsKey(kvp.Value))
                        {
                            _reverseDict[kvp.Value] = kvp.Key;
                        }
                    }
                    _logger.LogInformation($"[ModDictionaryService] Loaded {_dict.Count} mappings.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ModDictionaryService] Failed to load dictionary.");
            }
        }

        public string GetChineseName(string slugOrEnglishName)
        {
            if (string.IsNullOrWhiteSpace(slugOrEnglishName)) return null;
            
            var slug = slugOrEnglishName.ToLower().Replace(" ", "-").Replace("'", "");
            if (_dict.TryGetValue(slug, out var chineseName))
            {
                return chineseName;
            }
            return null;
        }

        public string TranslateChineseQueryToEnglish(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return query;

            if (!Regex.IsMatch(query, @"[\u4e00-\u9fa5]"))
            {
                return query; 
            }

            if (_reverseDict.TryGetValue(query, out var exactSlug))
            {
                return exactSlug.Replace("-", " ");
            }

            var bestMatch = _reverseDict.Keys
                .Where(k => k.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Length) 
                .FirstOrDefault();

            if (bestMatch != null)
            {
                var slug = _reverseDict[bestMatch];
                return slug.Replace("-", " ");
            }

            return query; 
        }
    }
}
