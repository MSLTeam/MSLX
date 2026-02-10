using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MSLX.Desktop.Converters;

/// <summary>
/// 状态数字转颜色
/// </summary>
public class StatusToBrushConverter : IMultiValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is int status)
        {
            return status switch
            {
                0 => Brushes.Gray,          // 未启动
                1 => Brushes.Orange,        // 启动中
                2 => Brushes.Green,         // 运行中
                3 => Brushes.IndianRed,     // 停止中
                4 => Brushes.DeepSkyBlue,   // 重启中
                _ => Brushes.DimGray
            };
        }
        return Brushes.Gray;
    }
}

/// <summary>
/// 核心名称格式化
/// </summary>
public class CoreNameConverter : IValueConverter
{
    public static readonly CoreNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string core)
        {
            string lower = core.ToLower();

            if (lower.Contains("neoforge")) return "NeoForge";
            if (lower.Contains("forge")) return "Forge";
            if (lower == "none") return "自定义模式";

            return core;
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}