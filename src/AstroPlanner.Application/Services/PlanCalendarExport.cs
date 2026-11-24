using System.Globalization;
using System.Text;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.Application.Services;

/// <summary>
/// Exports the scheduled blocks of "My plan" as an iCalendar file (RFC 5545, spec §4.6 [P1]):
/// one VEVENT per block, times in UTC, CRLF line ends, text escaped and lines folded at 75 octets.
/// The site name goes in LOCATION; coordinates never leave the app.
/// </summary>
public static class PlanCalendarExport
{
    private const string UtcFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <param name="toUtc">Converts a local wall-clock time of the site to UTC.</param>
    /// <param name="nowUtc">DTSTAMP of every event.</param>
    public static string BuildIcs(IEnumerable<PlanItemViewModel> items, Func<DateTime, DateTime> toUtc, DateTime nowUtc, string locationName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(toUtc);

        var ics = new StringBuilder();
        Line(ics, "BEGIN:VCALENDAR");
        Line(ics, "VERSION:2.0");
        Line(ics, "PRODID:-//AstroPlanner Pro//Session plan//EN");
        Line(ics, "CALSCALE:GREGORIAN");
        Line(ics, "METHOD:PUBLISH");
        foreach (var item in items)
        {
            if (item.StartLocal is not { } start || item.EndLocal is not { } end)
            {
                continue;
            }

            var startUtc = toUtc(start);
            var description = new List<string>();
            if (item.SubsText.Length > 0)
            {
                description.Add(DisplayNames.Format(Strings.IcsSubsFormat, item.SubsText, item.IntegrationText));
            }

            if (item.MeridianFlipLocal is { } flip)
            {
                description.Add(DisplayNames.Format(Strings.IcsMeridianFlipFormat, flip));
            }

            if (item.VerdictText.Length > 0)
            {
                description.Add(DisplayNames.Format(Strings.IcsVerdictFormat, item.VerdictText));
            }

            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                description.Add(item.Note.Trim());
            }

            Line(ics, "BEGIN:VEVENT");
            Line(ics, "UID:" + string.Create(CultureInfo.InvariantCulture,
                $"{startUtc.ToString(UtcFormat, CultureInfo.InvariantCulture)}-{Uid(item.Id)}@astroplanner-pro"));
            Line(ics, "DTSTAMP:" + nowUtc.ToString(UtcFormat, CultureInfo.InvariantCulture));
            Line(ics, "DTSTART:" + startUtc.ToString(UtcFormat, CultureInfo.InvariantCulture));
            Line(ics, "DTEND:" + toUtc(end).ToString(UtcFormat, CultureInfo.InvariantCulture));
            Line(ics, "SUMMARY:" + Escape(DisplayNames.Format(Strings.IcsSummaryFormat, item.Row.Title)));
            if (!string.IsNullOrWhiteSpace(locationName))
            {
                Line(ics, "LOCATION:" + Escape(locationName));
            }

            if (description.Count > 0)
            {
                Line(ics, "DESCRIPTION:" + Escape(string.Join("\n", description)));
            }

            Line(ics, "TRANSP:OPAQUE");
            Line(ics, "END:VEVENT");
        }

        Line(ics, "END:VCALENDAR");
        return ics.ToString();
    }

    /// <summary>RFC 5545 §3.3.11 TEXT escaping.</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace(";", @"\;", StringComparison.Ordinal)
            .Replace(",", @"\,", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", @"\n", StringComparison.Ordinal);
    }

    private static string Uid(string id) =>
        new([.. id.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);

    /// <summary>Appends a content line folded at 75 octets (RFC 5545 §3.1), never inside a UTF-8 sequence.</summary>
    private static void Line(StringBuilder ics, string line)
    {
        var octets = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > limit)
            {
                ics.Append("\r\n ");
                octets = 0;
                limit = 74;   // the leading space counts
            }

            ics.Append(rune.ToString());
            octets += size;
        }

        ics.Append("\r\n");
    }
}
