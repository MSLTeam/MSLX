using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MSLX.Desktop.Converters;

/// <summary>
/// 将一个 double（通常是容器高度）按 ConverterParameter 指定的比例进行缩放。
/// 用于让某个元素的 MaxHeight 跟随父容器高度动态变化，避免在小窗口下溢出。
/// </summary>
public class ProportionalHeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double height)
        {
            double ratio = 0.45; // 默认占可用高度的 45%
            if (parameter is string paramStr && double.TryParse(paramStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                ratio = parsed;
            }
            return Math.Max(0, height * ratio);
        }
        return 0d;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}