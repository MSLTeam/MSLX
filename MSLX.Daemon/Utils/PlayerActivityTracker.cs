using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using MSLX.SDK.Models.Instance;

namespace MSLX.Daemon.Utils;

public static class PlayerActivityTracker
{
    private const string FileName = "mslx_players.json";
    private const int MaxDailyHistoryDays = 30;
    private const int MaxHourlyHistoryHours = 48;
    private const int MaxTrackedPlayers = 1000;

    private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static object GetLock(string basePath) =>
        FileLocks.GetOrAdd(basePath, _ => new object());

    public static void RecordLogin(string basePath, string playerName, string? ip = null, string? uuid = null)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(playerName))
            return;

        lock (GetLock(basePath))
        {
            try
            {
                var filePath = Path.Combine(basePath, FileName);
                var data = LoadData(filePath);

                var now = DateTime.Now;
                var nowStr = now.ToString("yyyy-MM-dd HH:mm:ss");
                var todayStr = now.ToString("yyyy-MM-dd");
                var hourStr = now.ToString("yyyy-MM-dd-HH");

                // 如果没有提供 UUID，从 usercache.json 补充
                if (string.IsNullOrEmpty(uuid))
                {
                    uuid = TryGetUuidFromUserCache(basePath, playerName);
                }

                // 更新玩家记录
                if (data.Players.TryGetValue(playerName, out var playerItem))
                {
                    playerItem.LastLoginTime = nowStr;
                    if (!string.IsNullOrWhiteSpace(ip)) playerItem.LastIp = ip;
                    if (!string.IsNullOrWhiteSpace(uuid)) playerItem.Uuid = uuid;
                    playerItem.LoginCount++;
                }
                else
                {
                    data.Players[playerName] = new PlayerActivityItem
                    {
                        Name = playerName,
                        Uuid = uuid,
                        LastLoginTime = nowStr,
                        LastIp = ip,
                        LoginCount = 1
                    };
                }

                // 更新每日活跃集合
                if (!data.DailyActive.TryGetValue(todayStr, out var activeDailySet))
                {
                    activeDailySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    data.DailyActive[todayStr] = activeDailySet;
                }
                activeDailySet.Add(playerName);

                // 更新每小时活跃集合
                if (!data.HourlyActive.TryGetValue(hourStr, out var activeHourlySet))
                {
                    activeHourlySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    data.HourlyActive[hourStr] = activeHourlySet;
                }
                activeHourlySet.Add(playerName);

                // 数据量上限控制
                var expiredDates = data.DailyActive.Keys
                    .Where(k => DateTime.TryParse(k, out var d) && (DateTime.Today - d.Date).TotalDays > MaxDailyHistoryDays)
                    .ToList();
                foreach (var exp in expiredDates)
                {
                    data.DailyActive.Remove(exp);
                }

                // 丢掉超过 MaxHourlyHistoryHours 小时的记录
                var expiredHours = data.HourlyActive.Keys
                    .Where(k => DateTime.TryParseExact(k, "yyyy-MM-dd-HH", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) && (now - dt).TotalHours > MaxHourlyHistoryHours)
                    .ToList();
                foreach (var expH in expiredHours)
                {
                    data.HourlyActive.Remove(expH);
                }

                // 玩家数超过 MaxTrackedPlayers，按最后登录时间淘汰最旧的玩家
                if (data.Players.Count > MaxTrackedPlayers)
                {
                    var sortedOldest = data.Players.Values
                        .OrderBy(p => DateTime.TryParse(p.LastLoginTime, out var dt) ? dt : DateTime.MinValue)
                        .Take(data.Players.Count - MaxTrackedPlayers)
                        .Select(p => p.Name)
                        .ToList();

                    foreach (var oldName in sortedOldest)
                    {
                        data.Players.Remove(oldName);
                    }
                }

                SaveData(filePath, data);
            }
            catch
            {
                // 没事的～
            }
        }
    }

    public static PlayerHistoryResponse GetHistoryData(string basePath)
    {
        var response = new PlayerHistoryResponse();
        if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
            return response;

        lock (GetLock(basePath))
        {
            try
            {
                var filePath = Path.Combine(basePath, FileName);
                var activityData = LoadData(filePath);

                // 读 usercache.json
                var userCachePath = Path.Combine(basePath, "usercache.json");
                var userCacheList = new List<UserCacheItem>();
                if (File.Exists(userCachePath))
                {
                    var cacheJson = File.ReadAllText(userCachePath);
                    if (!string.IsNullOrWhiteSpace(cacheJson))
                    {
                        userCacheList = JsonSerializer.Deserialize<List<UserCacheItem>>(cacheJson, JsonOptions) ?? new();
                    }
                }

                // 合并玩家列表
                var merged = new Dictionary<string, UserCacheItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var uc in userCacheList)
                {
                    merged[uc.Name] = uc;
                }

                foreach (var (_, act) in activityData.Players)
                {
                    if (merged.TryGetValue(act.Name, out var existing))
                    {
                        existing.LastLoginTime = act.LastLoginTime;
                        existing.LastIp = act.LastIp;
                        existing.LoginCount = act.LoginCount;
                        if (string.IsNullOrEmpty(existing.Uuid) && !string.IsNullOrEmpty(act.Uuid))
                        {
                            existing.Uuid = act.Uuid;
                        }
                    }
                    else
                    {
                        merged[act.Name] = new UserCacheItem
                        {
                            Name = act.Name,
                            Uuid = act.Uuid ?? string.Empty,
                            LastLoginTime = act.LastLoginTime,
                            LastIp = act.LastIp,
                            LoginCount = act.LoginCount
                        };
                    }
                }

                // 按上次登录时间倒序排序
                response.Players = merged.Values
                    .OrderByDescending(p => DateTime.TryParse(p.LastLoginTime, out var dt) ? dt : DateTime.MinValue)
                    .ThenBy(p => p.Name)
                    .ToList();

                // 生成多粒度统计数据 (30天 / 14天 / 7天 / 1天 / 6小时)
                var rangeStats = new Dictionary<string, List<DailyActiveStat>>();

                // 30天 (按天)
                rangeStats["30d"] = GenerateDailyStats(activityData, 30);
                // 14天 (按天)
                rangeStats["14d"] = GenerateDailyStats(activityData, 14);
                // 7天 (按天)
                rangeStats["7d"] = GenerateDailyStats(activityData, 7);
                // 1天 (24小时，按小时)
                rangeStats["1d"] = GenerateHourlyStats(activityData, 24);
                // 6小时 (按小时)
                rangeStats["6h"] = GenerateHourlyStats(activityData, 6);

                response.RangeStats = rangeStats;
                response.ChartData = rangeStats["1d"];
            }
            catch
            {
                // 出错时返回空列表
            }
        }

        return response;
    }

    private static List<DailyActiveStat> GenerateDailyStats(PlayerActivityData activityData, int days)
    {
        var result = new List<DailyActiveStat>();
        var startDate = DateTime.Today.AddDays(-(days - 1));
        for (int i = 0; i < days; i++)
        {
            var day = startDate.AddDays(i);
            var dateKey = day.ToString("yyyy-MM-dd");
            var count = activityData.DailyActive.TryGetValue(dateKey, out var set) ? set.Count : 0;

            result.Add(new DailyActiveStat
            {
                Date = day.ToString("MM-dd"),
                Count = count
            });
        }
        return result;
    }

    private static List<DailyActiveStat> GenerateHourlyStats(PlayerActivityData activityData, int hours)
    {
        var result = new List<DailyActiveStat>();
        var now = DateTime.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var startHour = currentHour.AddHours(-(hours - 1));

        for (int i = 0; i < hours; i++)
        {
            var h = startHour.AddHours(i);
            var hourKey = h.ToString("yyyy-MM-dd-HH");
            var count = activityData.HourlyActive.TryGetValue(hourKey, out var set) ? set.Count : 0;

            result.Add(new DailyActiveStat
            {
                Date = h.ToString("HH:00"),
                Count = count
            });
        }
        return result;
    }

    private static PlayerActivityData LoadData(string filePath)
    {
        if (!File.Exists(filePath))
            return new PlayerActivityData();

        try
        {
            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json)) return new PlayerActivityData();
            return JsonSerializer.Deserialize<PlayerActivityData>(json, JsonOptions) ?? new PlayerActivityData();
        }
        catch
        {
            return new PlayerActivityData();
        }
    }

    private static void SaveData(string filePath, PlayerActivityData data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    private static string? TryGetUuidFromUserCache(string basePath, string playerName)
    {
        try
        {
            var userCachePath = Path.Combine(basePath, "usercache.json");
            if (!File.Exists(userCachePath)) return null;

            var json = File.ReadAllText(userCachePath);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var list = JsonSerializer.Deserialize<List<UserCacheItem>>(json, JsonOptions);
            var item = list?.FirstOrDefault(x => x.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
            return item?.Uuid;
        }
        catch
        {
            return null;
        }
    }
}
