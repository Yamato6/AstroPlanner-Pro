using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Catalog search, ignoring case and accents ("andromeda" finds "Andrómeda"):
/// <list type="bullet">
/// <item>A query that looks like an identifier ("M31", "m 31", "31", "ngc224", "NGC 224", "C14",
/// "ic 434", "PGC 2557", "Sh2-155") finds exactly that object, by its id or any cross-id
/// (otherwise "M1" would find M10-M19).</item>
/// <item>Anything else is looked for in the names and the constellation.</item>
/// </list>
/// The keys of every object are computed once (<see cref="Index"/>), so filtering ~13k objects
/// stays fast.
/// </summary>
public static partial class CatalogSearch
{
    /// <summary>Precomputed search keys of an object.</summary>
    /// <param name="IdentifierKeys">Compact identifiers: "m42", "ngc1976"…</param>
    /// <param name="Text">Normalized names and constellation.</param>
    public sealed record Entry(IReadOnlyList<string> IdentifierKeys, string Text);

    /// <summary>A parsed query.</summary>
    public sealed record Query(string? IdentifierKey, string Text)
    {
        public bool IsEmpty => IdentifierKey is null && Text.Length == 0;
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsOnly();

    [GeneratedRegex(@"^[a-z]+\d")]
    private static partial Regex LooksLikeIdentifier();

    public static Entry Index(CelestialObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new Entry(
            [.. target.AllIdentifiers.Select(CompactKey).Distinct()],
            Normalize(string.Join(" | ", target.AlternateNames.Prepend(target.DisplayName).Append(target.Constellation))));
    }

    public static Query Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Query(null, string.Empty);
        }

        var normalized = Normalize(text);
        var compact = CompactKey(normalized);

        // "31" means M31, as in AstroPlanner v1.
        if (DigitsOnly().IsMatch(compact))
        {
            return new Query("m" + int.Parse(compact, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), normalized);
        }

        return LooksLikeIdentifier().IsMatch(compact) ? new Query(compact, normalized) : new Query(null, normalized);
    }

    public static bool Matches(CelestialObjectRowViewModel item, Query query)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(query);
        return Matches(item.Search, query);
    }

    public static bool Matches(Entry entry, Query query)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(query);

        if (query.IsEmpty)
        {
            return true;
        }

        if (query.IdentifierKey is { } key)
        {
            foreach (var identifier in entry.IdentifierKeys)
            {
                if (identifier == key)
                {
                    return true;
                }
            }

            return false;
        }

        return entry.Text.Contains(query.Text, StringComparison.Ordinal);
    }

    /// <summary>Convenience overload: parses the query each time (not for loops over the catalog).</summary>
    public static bool Matches(CelestialObjectRowViewModel item, string? query) => Matches(item, Parse(query));

    /// <summary>"NGC 0224" → "ngc224", "Sh2-155" → "sh2155": lower case, no spaces, dashes or leading zeros.</summary>
    public static string CompactKey(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        var builder = new StringBuilder(identifier.Length);
        var previousIsDigit = false;
        foreach (var character in Normalize(identifier))
        {
            if (character is ' ' or '-' or '_')
            {
                continue;
            }

            // Leading zeros of a number ("ngc0224") are dropped.
            if (character == '0' && !previousIsDigit)
            {
                continue;
            }

            builder.Append(character);
            previousIsDigit = char.IsAsciiDigit(character);
        }

        return builder.ToString();
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
