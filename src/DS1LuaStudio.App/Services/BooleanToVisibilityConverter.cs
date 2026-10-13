using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DS1LuaStudio.App.Services;

/// <summary>Shows an element only when a flag is set, and hides it otherwise.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}