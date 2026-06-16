using System;
using System.Globalization;
using System.Windows.Data;

namespace MCServerLauncher.WPF.View.Converters;

public sealed class CardWidthConverter : IValueConverter
{
    public double MinWidth { get; set; } = 360;

    public double HorizontalGap { get; set; } = 8;

    public double ReservedWidth { get; set; } = 0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double availableWidth || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return MinWidth;
        }

        var usableWidth = Math.Max(0, availableWidth - ReservedWidth);
        if (usableWidth <= MinWidth)
        {
            return MinWidth;
        }

        var columns = Math.Max(1, (int)Math.Floor((usableWidth + HorizontalGap) / (MinWidth + HorizontalGap)));
        return Math.Max(MinWidth, (usableWidth - (columns - 1) * HorizontalGap) / columns);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
