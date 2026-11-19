using System.IO;
using System.Text.Json;

namespace AstroPlanner.UI.Services;

/// <summary>
/// JSON implementation of <see cref="ISettingsService"/>.
/// File: %LocalAppData%\AstroPlanner\settings.json
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.DataFile("settings.json");
    }

    public ObserverSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new ObserverSettings();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ObserverSettings>(json, Options) ?? new ObserverSettings();
        }
        catch (JsonException)
        {
            // Corrupt file: ignore it and use the defaults.
            return new ObserverSettings();
        }
        catch (IOException)
        {
            return new ObserverSettings();
        }
    }

    public void Save(ObserverSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(settings, Options));
    }
}
