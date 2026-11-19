using System.Text.Json;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Data.Import;

/// <summary>What was read from an AstroPlanner v1 data folder.</summary>
public sealed record V1Data(
    ObserverSettings? Settings,
    IReadOnlyList<ObservationPlanEntry> Plan,
    IReadOnlyList<ObservationLogEntry> Logbook,
    IReadOnlyList<string> Warnings)
{
    public bool IsEmpty => Settings is null && Plan.Count == 0 && Logbook.Count == 0;
}

/// <summary>What <see cref="V1Importer.Apply"/> added.</summary>
public sealed record V1ImportResult(bool SettingsImported, int PlanItemsAdded, int LogEntriesAdded, Location? Location, IReadOnlyList<string> Warnings);

/// <summary>
/// Imports the data of AstroPlanner v1 (<c>%LocalAppData%\AstroPlanner</c>): settings.json,
/// plan.json and logbook.json. The v1 files are only read, never modified (spec §7.4).
/// The JSON keys are the same as in v1; what changes are some values: planet ids were Spanish
/// ("Marte"), object names in the logbook were Spanish, and plans before v0.6 were a plain list
/// of ids. Corrupt files are skipped with a warning; files set aside by v1
/// (<c>logbook.corrupto-*.json</c>) are ignored.
/// </summary>
public static class V1Importer
{
    /// <summary>Name of the location created from the v1 settings.</summary>
    public const string ImportedLocationName = "Imported from AstroPlanner v1";

    private static readonly Dictionary<string, string> PlanetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mercurio"] = "Mercury",
        ["Venus"] = "Venus",
        ["Marte"] = "Mars",
        ["Júpiter"] = "Jupiter",
        ["Jupiter"] = "Jupiter",
        ["Saturno"] = "Saturn",
        ["Urano"] = "Uranus",
        ["Neptuno"] = "Neptune"
    };

    private static readonly string[] DataFiles = ["settings.json", "plan.json", "logbook.json"];

    private static readonly JsonSerializerOptions SettingsOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>The v1 data folder under a given %LocalAppData%.</summary>
    public static string DefaultDirectory(string localApplicationData) =>
        Path.Combine(localApplicationData, "AstroPlanner");

    /// <summary>True if <paramref name="directory"/> contains any v1 data file.</summary>
    public static bool HasData(string directory) =>
        Directory.Exists(directory)
        && DataFiles.Any(f => File.Exists(Path.Combine(directory, f)));

    /// <summary>
    /// Reads the v1 data. Object ids are mapped to this version and names re-resolved against
    /// <paramref name="catalog"/> (which should include the planets).
    /// </summary>
    public static V1Data Read(string directory, IReadOnlyCollection<CelestialObject> catalog)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(catalog);

        var byId = catalog.GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        var settings = ReadFile(directory, "settings.json", warnings, ReadSettings);
        var plan = ReadFile(directory, "plan.json", warnings, json => ReadPlan(json, byId, warnings)) ?? [];
        var logbook = ReadFile(directory, "logbook.json", warnings, json => ReadLogbook(json, byId, warnings)) ?? [];

        return new V1Data(settings, plan, logbook, warnings);
    }

    /// <summary>
    /// Adds the v1 data to this version's stores: the settings replace the current ones (and
    /// become a saved location), plan items and logbook entries that are not there yet are
    /// appended.
    /// </summary>
    public static V1ImportResult Apply(
        V1Data data,
        ISettingsService settings,
        IObservationPlanService plan,
        IObservationLogService logbook,
        ILocationRepository locations)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(logbook);
        ArgumentNullException.ThrowIfNull(locations);

        Location? location = null;
        if (data.Settings is { } imported)
        {
            var windowsId = TimeZoneConversion.ToWindowsId(imported.TimeZoneId) ?? imported.TimeZoneId;
            var ianaId = TimeZoneConversion.ToIanaId(imported.TimeZoneId) ?? "Etc/UTC";
            location = new Location(
                "v1-import", ImportedLocationName, imported.LatitudeDegrees, imported.LongitudeDegrees,
                imported.ElevationMeters, ianaId, windowsId);
            locations.Save(location);

            imported.TimeZoneId = windowsId;
            imported.ActiveLocationId = location.Id;
            settings.Save(imported);
        }

        var currentPlan = plan.Load();
        var newPlanItems = data.Plan
            .Where(item => !currentPlan.Any(existing => string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (newPlanItems.Count > 0)
        {
            plan.Save(currentPlan.Concat(newPlanItems));
        }

        var currentLog = logbook.Load();
        var known = currentLog.Select(e => e.Id).ToHashSet();
        var newLogEntries = data.Logbook.Where(e => !known.Contains(e.Id)).ToList();
        if (newLogEntries.Count > 0)
        {
            logbook.Save(currentLog.Concat(newLogEntries));
        }

        return new V1ImportResult(data.Settings is not null, newPlanItems.Count, newLogEntries.Count, location, data.Warnings);
    }

    /// <summary>The id of this version for a v1 id ("Marte" → "Mars"; Messier ids are unchanged).</summary>
    public static string MapObjectId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var trimmed = id.Trim();
        return PlanetIds.GetValueOrDefault(trimmed) ?? trimmed;
    }

    private static T? ReadFile<T>(string directory, string fileName, List<string> warnings, Func<string, T?> parse)
        where T : class
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            warnings.Add($"{fileName} could not be read and was skipped ({ex.GetType().Name}).");
            return null;
        }
    }

    private static ObserverSettings? ReadSettings(string json)
    {
        var settings = JsonSerializer.Deserialize<ObserverSettings>(json, SettingsOptions);
        if (settings is null)
        {
            return null;
        }

        // Validates the coordinates (throws ArgumentOutOfRangeException → skipped).
        try
        {
            _ = settings.ToObserverLocation();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new FormatException("Invalid coordinates.", ex);
        }

        settings.ActiveLocationId = null;
        return settings;
    }

    private static List<ObservationPlanEntry> ReadPlan(string json, Dictionary<string, CelestialObject> catalog, List<string> warnings)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The plan is not a list.");
        }

        var entries = new List<ObservationPlanEntry>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var entry = element.ValueKind switch
            {
                // Plans before v0.6: just the ids.
                JsonValueKind.String => new ObservationPlanEntry(element.GetString()!),
                JsonValueKind.Object => element.Deserialize<ObservationPlanEntry>(CamelCaseOptions),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(entry?.Id))
            {
                continue;
            }

            var id = MapObjectId(entry.Id);
            if (!catalog.ContainsKey(id))
            {
                warnings.Add($"Plan item '{entry.Id}' is not in the catalog.");
            }

            if (!entries.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new ObservationPlanEntry(id, entry.Note ?? string.Empty));
            }
        }

        return entries;
    }

    private static List<ObservationLogEntry> ReadLogbook(string json, Dictionary<string, CelestialObject> catalog, List<string> warnings)
    {
        var entries = JsonSerializer.Deserialize<List<ObservationLogEntry>>(json, CamelCaseOptions) ?? [];
        var result = new List<ObservationLogEntry>();
        foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.ObjectId)))
        {
            var id = MapObjectId(entry.ObjectId);
            var name = catalog.TryGetValue(id, out var target) ? target.DisplayName : entry.ObjectName ?? id;
            if (target is null)
            {
                warnings.Add($"Logbook entry for '{entry.ObjectId}' is not in the catalog; its name was kept.");
            }

            result.Add(entry with
            {
                Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
                ObjectId = id,
                ObjectName = name,
                Location = entry.Location ?? string.Empty,
                Equipment = entry.Equipment ?? string.Empty,
                Notes = entry.Notes ?? string.Empty
            });
        }

        return result;
    }
}
