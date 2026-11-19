using System.Globalization;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.DataBuilder;

/// <summary>Curated corrections and English names applied on top of OpenNGC (data/curated/messier.json).</summary>
/// <param name="CommonNames">Messier id → English common name (the names used since AstroPlanner v1).</param>
/// <param name="MessierOverrides">
/// Messier id → OpenNGC name of the object that carries it, where OpenNGC differs from the
/// usual convention (M102: OpenNGC treats it as a duplicate of M101; the app follows the common
/// identification with NGC 5866).
/// </param>
internal sealed record MessierCuration(
    IReadOnlyDictionary<string, string> CommonNames,
    IReadOnlyDictionary<string, string> MessierOverrides);

/// <summary>Result of importing OpenNGC.</summary>
internal sealed record OpenNgcImport(
    IReadOnlyList<CatalogEntry> Objects,
    int DuplicateRows,
    int NonExistentRows,
    IReadOnlyList<string> ConstellationMismatches,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Converts OpenNGC (NGC.csv + addendum.csv) into catalog entries: maps the object types,
/// folds the "Dup" rows into their target as extra designations, drops "NonEx" rows, picks
/// the primary id (Messier > Caldwell-only > OpenNGC name) and recomputes the constellation.
/// </summary>
internal static class OpenNgcImporter
{
    public static OpenNgcImport Import(IEnumerable<IReadOnlyDictionary<string, string>> rows, MessierCuration curation)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(curation);

        var overridesByName = curation.MessierOverrides.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var overriddenMessier = new HashSet<string>(curation.MessierOverrides.Keys, StringComparer.Ordinal);

        var entries = new List<CatalogEntry>();
        var duplicates = new List<IReadOnlyDictionary<string, string>>();
        var mismatches = new List<string>();
        var warnings = new List<string>();
        var nonExistent = 0;

        foreach (var row in rows)
        {
            switch (row["Type"])
            {
                case "NonEx":
                    nonExistent++;
                    continue;
                case "Dup":
                    duplicates.Add(row);
                    continue;
            }

            var entry = CreateEntry(row, overridesByName, overriddenMessier, curation);

            // OpenNGC splits Serpens into Se1 (Caput) and Se2 (Cauda); the IAU abbreviation is Ser.
            var openNgcConstellation = row["Const"] is "Se1" or "Se2" ? "Ser" : row["Const"];
            if (!string.Equals(openNgcConstellation, entry.Constellation, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(openNgcConstellation))
            {
                mismatches.Add($"{entry.Id}: OpenNGC {openNgcConstellation}, computed {entry.Constellation}");
            }

            entries.Add(entry);
        }

        // A cross-reference that is another object's own id is ambiguous (e.g. M71 = NGC 6838 also
        // lists NGC 6839, an object of its own): the object that owns the id keeps it.
        var primaryIds = new HashSet<string>(entries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            entry.Designations.RemoveAll(d => primaryIds.Contains(d.Value));
        }

        var index = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            foreach (var key in entry.Designations.Select(d => d.Value).Prepend(entry.Id))
            {
                if (!index.TryAdd(key, entry) && !ReferenceEquals(index[key], entry))
                {
                    warnings.Add($"'{key}' names both {index[key].Id} and {entry.Id}");
                }
            }
        }

        foreach (var duplicate in duplicates)
        {
            FoldDuplicate(duplicate, index, overriddenMessier, warnings);
        }

        var ids = entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (ids.Count > 0)
        {
            throw new InvalidDataException("Duplicate primary ids: " + string.Join(", ", ids));
        }

        return new OpenNgcImport(
            [.. entries.OrderBy(e => e.Id, StringComparer.Ordinal)],
            duplicates.Count, nonExistent, mismatches, warnings);
    }

    private static CatalogEntry CreateEntry(
        IReadOnlyDictionary<string, string> row,
        Dictionary<string, string> overridesByName,
        HashSet<string> overriddenMessier,
        MessierCuration curation)
    {
        var name = row["Name"];
        var own = Designations.Format(name);
        var designations = new List<Designation>();
        void Add(Designation designation)
        {
            if (Designations.IsKept(designation) && !designations.Contains(designation))
            {
                designations.Add(designation);
            }
        }

        // Messier: the M column, unless curated differently.
        if (overridesByName.TryGetValue(name, out var curatedMessier))
        {
            Add(new Designation(curatedMessier, "M"));
        }
        else if (row["M"] is { Length: > 0 } messier && !overriddenMessier.Contains(Designations.Messier(messier).Value))
        {
            Add(Designations.Messier(messier));
        }

        if (own.Catalog != "M")
        {
            Add(own);
        }

        foreach (var ngc in Split(row["NGC"]))
        {
            Add(Designations.CrossReference("NGC", ngc));
        }

        foreach (var ic in Split(row["IC"]))
        {
            Add(Designations.CrossReference("IC", ic));
        }

        foreach (var identifier in Split(row["Identifiers"]))
        {
            Add(Designations.Format(identifier));
        }

        // Primary id: Messier, else a Caldwell-only object of the addendum, else the own name.
        var primary = designations.FirstOrDefault(d => d.Catalog == "M")
            ?? (own.Catalog == "C" ? own : null)
            ?? own;
        designations.Remove(primary);

        var coordinates = new EquatorialCoordinates(ParseRightAscension(row["RA"]), ParseDeclination(row["Dec"]));
        var vMagnitude = ParseOptional(row["V-Mag"]);
        var bMagnitude = ParseOptional(row["B-Mag"]);
        var names = Split(row["Common names"]).ToList();
        if (curation.CommonNames.TryGetValue(primary.Value, out var curatedName))
        {
            names.RemoveAll(n => string.Equals(n, curatedName, StringComparison.OrdinalIgnoreCase));
            names.Insert(0, curatedName);
        }

        return new CatalogEntry
        {
            Id = primary.Value,
            Type = MapType(row["Type"], name),
            RightAscensionHours = coordinates.RightAscensionHours,
            DeclinationDegrees = coordinates.DeclinationDegrees,
            Constellation = ConstellationLookup.FromJ2000(coordinates).Abbreviation,
            Magnitude = vMagnitude ?? bMagnitude,
            MagnitudeBand = vMagnitude is not null ? "V" : bMagnitude is not null ? "B" : null,
            SurfaceBrightness = ParseOptional(row["SurfBr"]),
            MajorAxisArcmin = ParseOptional(row["MajAx"]),
            MinorAxisArcmin = ParseOptional(row["MinAx"]),
            PositionAngleDegrees = ParseOptional(row["PosAng"]),
            CommonName = names.FirstOrDefault() ?? string.Empty,
            AlternateNames = [.. names.Skip(1)],
            Designations = designations,
            Source = "OpenNGC:" + name
        };
    }

    /// <summary>Adds the identifiers of a "Dup" row to the object it duplicates.</summary>
    private static void FoldDuplicate(
        IReadOnlyDictionary<string, string> row,
        Dictionary<string, CatalogEntry> index,
        HashSet<string> overriddenMessier,
        List<string> warnings)
    {
        var name = row["Name"];
        var own = Designations.Format(name);
        if (own.Catalog == "M" && overriddenMessier.Contains(own.Value))
        {
            return;
        }

        var targets = Split(row["M"]).Select(Designations.Messier)
            .Concat(Split(row["NGC"]).Select(n => Designations.CrossReference("NGC", n)))
            .Concat(Split(row["IC"]).Select(n => Designations.CrossReference("IC", n)));
        var target = targets.Select(t => index.GetValueOrDefault(t.Value)).FirstOrDefault(t => t is not null);

        if (target is null)
        {
            warnings.Add($"Duplicate {name} points to no known object");
            return;
        }

        if (!string.Equals(target.Id, own.Value, StringComparison.OrdinalIgnoreCase) && !target.Designations.Contains(own))
        {
            target.Designations.Add(own);
            index.TryAdd(own.Value, target);
        }
    }

    private static CelestialObjectType MapType(string type, string name) => type switch
    {
        "G" => CelestialObjectType.Galaxy,
        "GPair" or "GTrpl" or "GGroup" => CelestialObjectType.GalaxyGroup,
        "OCl" => CelestialObjectType.OpenCluster,
        "GCl" => CelestialObjectType.GlobularCluster,
        "Cl+N" => CelestialObjectType.ClusterWithNebula,
        "*Ass" => CelestialObjectType.StellarAssociation,
        "PN" => CelestialObjectType.PlanetaryNebula,
        "SNR" => CelestialObjectType.SupernovaRemnant,
        "HII" => CelestialObjectType.HiiRegion,
        "EmN" => CelestialObjectType.EmissionNebula,
        "RfN" => CelestialObjectType.ReflectionNebula,
        "DrkN" => CelestialObjectType.DarkNebula,
        "Neb" => CelestialObjectType.DiffuseNebula,
        "*" => CelestialObjectType.Star,
        "**" => CelestialObjectType.DoubleStar,
        "Nova" => CelestialObjectType.Nova,
        "Other" => CelestialObjectType.Unknown,
        _ => throw new InvalidDataException($"Unknown OpenNGC type '{type}' for {name}.")
    };

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static double? ParseOptional(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>"HH:MM:SS.ss" → hours.</summary>
    internal static double ParseRightAscension(string value)
    {
        var parts = value.Split(':');
        var hours = double.Parse(parts[0], CultureInfo.InvariantCulture)
                    + double.Parse(parts[1], CultureInfo.InvariantCulture) / 60.0
                    + double.Parse(parts[2], CultureInfo.InvariantCulture) / 3600.0;
        return hours >= 24 ? hours - 24 : hours;
    }

    /// <summary>"+DD:MM:SS.s" → degrees.</summary>
    internal static double ParseDeclination(string value)
    {
        var sign = value.StartsWith('-') ? -1.0 : 1.0;
        var parts = value.TrimStart('+', '-').Split(':');
        return sign * (double.Parse(parts[0], CultureInfo.InvariantCulture)
                       + double.Parse(parts[1], CultureInfo.InvariantCulture) / 60.0
                       + double.Parse(parts[2], CultureInfo.InvariantCulture) / 3600.0);
    }
}
