using System;
using System.Collections.Generic;
using System.IO;
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
                var path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "MSLX_ModDictionary.json");
                if (!File.Exists(path))
                {
                    _logger.LogWarning($"[ModDictionaryService] Dictionary file not found at {path}");
                    return;
                }

                var json = File.ReadAllText(path);
                var rawDict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
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
