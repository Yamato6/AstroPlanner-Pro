using System.Globalization;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Services;

/// <summary>Exports "My plan" to CSV (see <see cref="CsvBuilder"/>), one row per target in observing order.</summary>
public static class PlanExport
{
    public static string BuildCsv(IEnumerable<PlanItemViewModel> items, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(culture);

        var csv = new CsvBuilder(culture).AddRow([Strings.CsvOrder, Strings.CsvId, Strings.CsvName,
            Strings.CsvConstellation, Strings.CsvType, Strings.CsvMagnitude, Strings.CsvStart, Strings.CsvEnd,
            Strings.CsvSubCount, Strings.CsvSubSeconds, Strings.CsvIsoGain, Strings.CsvIntegrationMinutes,
            Strings.CsvMeridianFlip, Strings.CsvVerdict, Strings.CsvCulmination, Strings.CsvMaximumAltitude,
            Strings.CsvMoonDistance, Strings.CsvMoonInterferes, Strings.CsvNotes]);

        var order = 1;
        foreach (var item in items)
        {
            var row = item.Row;
            var schedule = item.Schedule;
            var hasSubs = schedule is { SubCount: > 0 };
            csv.AddRow([
                order++.ToString(culture),
                row.Id,
                row.CommonName,
                row.Constellation,
                DisplayNames.Of(row.Type),
                row.ApparentMagnitude.ToString("0.0", culture),
                item.StartLocal?.ToString("HH:mm", culture) ?? "",
                item.EndLocal?.ToString("HH:mm", culture) ?? "",
                hasSubs ? schedule!.SubCount.ToString(culture) : "",
                hasSubs ? schedule!.SubSeconds.ToString("0.#", culture) : "",
                hasSubs ? (schedule!.Iso?.ToString(culture) ?? schedule.Gain?.ToString(culture) ?? "") : "",
                hasSubs ? schedule!.Integration.TotalMinutes.ToString("0", culture) : "",
                item.MeridianFlipLocal?.ToString("HH:mm", culture) ?? "",
                item.VerdictText,
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
