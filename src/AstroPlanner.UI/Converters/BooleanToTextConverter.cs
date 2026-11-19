using System.Globalization;
using System.Windows.Data;

namespace AstroPlanner.UI.Converters;

/// <summary>Shows <see cref="TrueText"/> or <see cref="FalseText"/> depending on a boolean.</summary>
public sealed class BooleanToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "";
    public string FalseText { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
