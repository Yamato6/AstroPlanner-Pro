using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Catalog search by id, name or constellation, ignoring case and accents
/// ("andromeda" finds "Andrómeda").
/// </summary>
public static partial class CatalogSearch
{
    [GeneratedRegex(@"^m?(\d+)$")]
    private static partial Regex IdQuery();

    public static bool Matches(CelestialObjectRowViewModel item, string? query)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Matches(item.Id, item.CommonName, item.Constellation, query);
    }

    public static bool Matches(string id, string commonName, string constellation, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalizedQuery = Normalize(query);

        // "M31", "m 31" or "31" look for the exact id (otherwise "M1" would find M10-M19).
        var idMatch = IdQuery().Match(normalizedQuery.Replace(" ", "", StringComparison.Ordinal));
        if (idMatch.Success)
        {
            return Normalize(id) == "m" + int.Parse(idMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        return Normalize(commonName).Contains(normalizedQuery, StringComparison.Ordinal)
            || Normalize(constellation).Contains(normalizedQuery, StringComparison.Ordinal);
    }

    /// <summary>Lower case, without accents and without extra spaces.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
