using System.Globalization;
using System.Windows.Data;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.Converters;

/// <summary>
/// User-facing text (see <see cref="DisplayNames"/>): the name of an object type, or for a logbook
/// entry its kind (observed / photographed), or its capture with ConverterParameter=capture.
/// </summary>
public sealed class DisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CelestialObjectType type => DisplayNames.Of(type),
        ObservationLogEntry entry when parameter as string == "capture" => DisplayNames.Capture(entry),
        ObservationLogEntry entry => DisplayNames.LogKind(entry),
        _ => value
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
