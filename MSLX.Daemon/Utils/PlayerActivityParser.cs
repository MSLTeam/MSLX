using System.Text.RegularExpressions;

namespace MSLX.Daemon.Utils;

public enum PlayerActivityType
{
    None,
    Joined,
    Left
}

/// <summary>
/// 一次玩家进退事件的解析结果。Type 为 None 时表示该行日志不包含有效玩家活动。
/// </summary>
public record PlayerActivity(PlayerActivityType Type, string PlayerName, string? PlayerIp)
{
    public static readonly PlayerActivity None = new(PlayerActivityType.None, string.Empty, null);
}

/// <summary>
/// 从 MC 服务端日志行中解析玩家加入/离开事件（纯函数，无任何副作用）。
/// 会剔除 ANSI 颜色代码，并过滤假人（名称/IP 含方括号内容或 "local"）。
/// </summary>
public static class PlayerActivityParser
{
    // 匹配玩家进入/离开的正则表达式
    private static readonly Regex PlayerJoinedRegex =
        new Regex(@"\]:\s*(?<player>.+?)\[(?<ip>.*?)\]\slogged\sin\swith\sentity\sid", RegexOptions.Compiled);

    private static readonly Regex PlayerLeftRegex =
        new Regex(@"\]:\s*(?<player>.+?)\slost\sconnection:", RegexOptions.Compiled);

    private static readonly Regex FakePlayerFilterRegex =
        new Regex(@"\[.*\]|local", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AnsiColorRegex = new Regex(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    /// <summary>
    /// 判断名称或 IP 是否属于假人（含方括号内容或 "local"，忽略大小写）
    /// </summary>
    public static bool IsFakePlayer(string nameOrIp) => FakePlayerFilterRegex.IsMatch(nameOrIp);

    /// <summary>
    /// 解析一行日志。优先匹配加入事件，其次匹配离开事件，都不匹配返回 <see cref="PlayerActivity.None"/>。
    /// </summary>
    public static PlayerActivity Parse(string logLine)
    {
        if (string.IsNullOrEmpty(logLine))
            return PlayerActivity.None;

        // 预检
        if (!logLine.Contains("logged in with entity id") && !logLine.Contains("lost connection:"))
            return PlayerActivity.None;

        // 去掉ansi颜色代码
        string cleanLog = AnsiColorRegex.Replace(logLine, "");

        // 匹配加入
        var joinMatch = PlayerJoinedRegex.Match(cleanLog);
        if (joinMatch.Success)
        {
            string playerName = joinMatch.Groups["player"].Value.Trim();
            var rawIp = joinMatch.Groups["ip"].Value.Trim();

            // 排除假人
            if (IsFakePlayer(playerName) || IsFakePlayer(rawIp))
                return PlayerActivity.None;

            string? playerIp = null;
            if (!string.IsNullOrEmpty(rawIp))
            {
                if (rawIp.StartsWith("/")) rawIp = rawIp.TrimStart('/');
                int colonIdx = rawIp.LastIndexOf(':');
                if (colonIdx > 0) rawIp = rawIp.Substring(0, colonIdx);
                playerIp = rawIp;
            }

            return new PlayerActivity(PlayerActivityType.Joined, playerName, playerIp);
        }

        // 匹配离开
        var leftMatch = PlayerLeftRegex.Match(cleanLog);
        if (leftMatch.Success)
        {
            string playerName = leftMatch.Groups["player"].Value.Trim();
            if (IsFakePlayer(playerName))
                return PlayerActivity.None;

            return new PlayerActivity(PlayerActivityType.Left, playerName, null);
        }

        return PlayerActivity.None;
    }
}
