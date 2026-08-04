using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MSLX.Desktop.Converters
{
    public class FrpTunnelStatusToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string status = value?.ToString() ?? string.Empty;
            if (status == "在线")
            {
                return SolidColorBrush.Parse("#4CAF50"); // 绿色
            }
            return SolidColorBrush.Parse("#9E9E9E"); // 灰色
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
