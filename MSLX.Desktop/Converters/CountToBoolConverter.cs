using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MSLX.Desktop.Converters;

/// <summary>
/// 集合计数转布尔值：Count > 0 返回 true
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    public static readonly CountToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 集合计数转布尔值（取反）：Count == 0 返回 true
/// </summary>
public class CountToInverseBoolConverter : IValueConverter
{
    public static readonly CountToInverseBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
