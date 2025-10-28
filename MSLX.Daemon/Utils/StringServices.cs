using System;

namespace MSLX.Daemon.Utils;

public class StringServices
{
    private static readonly Random _random = new Random();

    /// <summary>
    /// 生成指定长度的随机字符串（可选前缀）
    /// </summary>
    /// <param name="length">随机字符串长度</param>
    /// <param name="prefix">可选前缀（默认无）</param>
    public static string GenerateRandomString(int length, string? prefix = null)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "长度不能为负数");
        }

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var randomChars = new char[length];
        for (int i = 0; i < length; i++)
        {
            randomChars[i] = chars[_random.Next(chars.Length)];
        }

        return (prefix ?? "") + new string(randomChars);
    }

    /// <summary>
    /// 生成指定范围内的随机整数（包含起始和结束值）
    /// </summary>
    public static int GetRandomNumber(int start, int end)
    {
        if (start > end)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "起始值不能大于结束值");
        }
        return _random.Next(start, end + 1);
    }

    /// <summary>
    /// 将秒级时间戳转换为DateTime类型（基于UTC+8时区）
    /// </summary>
    public static DateTime SecondsToDateTime(long seconds)
    {
        // 使用Unix时间戳起点1970-01-01 UTC
        DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime utcTime = origin.AddSeconds(seconds);
        TimeZoneInfo cstZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, cstZone);
    }

}