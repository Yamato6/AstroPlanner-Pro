using System.IO;
using System.Text.Json;

namespace AstroPlanner.UI.Services;

/// <summary>
/// JSON implementation of <see cref="IObservationPlanService"/>.
/// File: %LocalAppData%\AstroPlannerPro\plan.json
/// Current format: [{"id":"M31","note":"..."}]. It also reads the 0.3-0.5 format, a list of
/// ids (["M31","M42"]), so that existing plans are not lost.
/// </summary>
public sealed class JsonObservationPlanService : IObservationPlanService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public JsonObservationPlanService(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.DataFile("plan.json");
    }

    public IReadOnlyList<ObservationPlanEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_filePath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var entries = new List<ObservationPlanEntry>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var entry = element.ValueKind switch
                {
                    // Old format: the id only.
                    JsonValueKind.String => new ObservationPlanEntry(element.GetString()!),
                    JsonValueKind.Object => element.Deserialize<ObservationPlanEntry>(Options),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(entry?.Id))
                {
                    entries.Add(entry with { Note = entry.Note ?? "" });
                }
            }

            return entries;
        }
        catch (JsonException)
        {
            // Corrupt file: start with an empty plan.
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<ObservationPlanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(entries.ToList(), Options));
    }
}
