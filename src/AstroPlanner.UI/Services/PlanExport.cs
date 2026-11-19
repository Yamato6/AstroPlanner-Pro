using System.Globalization;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Services;

/// <summary>Exports "My plan" to CSV (see <see cref="CsvBuilder"/>).</summary>
public static class PlanExport
{
    public static string BuildCsv(IEnumerable<PlanItemViewModel> items, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(culture);

        var csv = new CsvBuilder(culture).AddRow([Strings.CsvOrder, Strings.CsvId, Strings.CsvName,
            Strings.CsvConstellation, Strings.CsvType, Strings.CsvMagnitude, Strings.CsvCulmination,
            Strings.CsvMaximumAltitude, Strings.CsvMoonDistance, Strings.CsvMoonInterferes, Strings.CsvNotes]);

        var order = 1;
        foreach (var item in items)
        {
            var row = item.Row;
            csv.AddRow([
                order++.ToString(culture),
                row.Id,
                row.CommonName,
                row.Constellation,
                DisplayNames.Of(row.Type),
                row.ApparentMagnitude.ToString("0.0", culture),
                item.CulminationLocal?.ToString("HH:mm", culture) ?? "",
                item.MaximumAltitudeDegrees.ToString("0", culture),
                row.MoonSeparationDegrees.ToString("0", culture),
                row.MoonInterferes ? Strings.Yes : Strings.No,
                item.Note
            ]);
        }

        return csv.ToString();
    }
}
