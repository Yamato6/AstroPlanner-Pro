using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AstroPlanner.UI.Services;

/// <summary>
/// An observation recorded in the logbook. Seeing, transparency and rating go from 1 (poor)
/// to 5 (excellent).
/// </summary>
public sealed record ObservationLogEntry(
    Guid Id,
    string ObjectId,
    string ObjectName,
    DateTime ObservedAtLocal,
    string Location,
    string Equipment,
    int Seeing,
    int Transparency,
    int Rating,
    string Notes)
{
    public string RatingStars => new string('★', Math.Clamp(Rating, 0, 5)) + new string('☆', 5 - Math.Clamp(Rating, 0, 5));
}

/// <summary>Persistence of the observation logbook.</summary>
public interface IObservationLogService
{
    /// <summary>Reads the logbook; an empty list if it does not exist or is invalid.</summary>
    IReadOnlyList<ObservationLogEntry> Load();

    /// <summary>Saves the logbook atomically.</summary>
    void Save(IEnumerable<ObservationLogEntry> entries);
}

/// <summary>
/// JSON implementation of <see cref="IObservationLogService"/>.
/// File: %LocalAppData%\AstroPlannerPro\logbook.json
/// </summary>
public sealed class JsonObservationLogService : IObservationLogService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public JsonObservationLogService(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.DataFile("logbook.json");
    }

    public IReadOnlyList<ObservationLogEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize<List<ObservationLogEntry>>(File.ReadAllText(_filePath), Options);
            return entries?.Where(entry => !string.IsNullOrWhiteSpace(entry.ObjectId)).ToList() ?? [];
        }
        catch (JsonException)
        {
            // Corrupt file: set it aside (so that the next saved observation does not overwrite
            // it) and start with an empty logbook.
            PreserveCorruptFile();
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<ObservationLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(entries.ToList(), Options));
    }

    /// <summary>Path the last corrupt file was moved to, if any.</summary>
    public string? PreservedCorruptFilePath { get; private set; }

    private void PreserveCorruptFile()
    {
        try
        {
            var preserved = Path.Combine(
                Path.GetDirectoryName(_filePath) ?? "",
                string.Create(CultureInfo.InvariantCulture,
                    $"{Path.GetFileNameWithoutExtension(_filePath)}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
            File.Move(_filePath, preserved);
            PreservedCorruptFilePath = preserved;
        }
        catch (IOException)
        {
            // If it cannot be moved, leave it as it is.
        }
    }
}
