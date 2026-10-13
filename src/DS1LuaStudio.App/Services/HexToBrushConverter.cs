using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DS1LuaStudio.App.Services;

/// <summary>Turns "#AARRGGBB" strings into a brush so colour presets can show a swatch.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex) is Color color)
                    return new SolidColorBrush(color);
            }
            catch
            {
                // fall through to a neutral swatch
            }
        }
        return new SolidColorBrush(Color.FromRgb(60, 64, 72));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
