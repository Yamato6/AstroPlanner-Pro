using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AstroPlanner.UI.Converters;

/// <summary>
/// Like WPF's BooleanToVisibilityConverter, but with <see cref="Invert"/> to hide an element
/// when the value is true.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
