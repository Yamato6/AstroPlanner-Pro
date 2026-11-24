using System.Globalization;
using System.Text;

namespace AstroPlanner.Application.Services;

/// <summary>
/// Builds CSV with the list separator of the given culture (the system's when exporting),
/// so that Excel opens it in columns without extra steps.
/// </summary>
internal sealed class CsvBuilder(CultureInfo culture)
{
    private readonly StringBuilder _builder = new();
    private readonly string _separator = culture.TextInfo.ListSeparator;

    public CultureInfo Culture { get; } = culture;

    public CsvBuilder AddRow(IEnumerable<string> fields)
    {
        _builder.AppendLine(string.Join(_separator, fields.Select(Escape)));
        return this;
    }

    public override string ToString() => _builder.ToString();

    /// <summary>Quotes fields that contain the separator, quotes or line breaks (RFC 4180).</summary>
    private string Escape(string field)
    {
        if (field.Contains(_separator, StringComparison.Ordinal)
            || field.Contains('"', StringComparison.Ordinal)
            || field.Contains('\n', StringComparison.Ordinal)
            || field.Contains('\r', StringComparison.Ordinal))
        {
            return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return field;
    }
}
