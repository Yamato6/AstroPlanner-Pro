using System.IO;
using AstroPlanner.Data.Import;
using AstroPlanner.Data.Storage;
using AstroPlanner.UI.Resources;

namespace AstroPlanner.UI.Services;

/// <summary>
/// Offers, once, to import the data of AstroPlanner v1 (spec §7.4). The offer is made at
/// startup, before the view models load the settings, when v1 data exists and it has not been
/// offered before (accepted or not). The v1 files are only read.
/// </summary>
public sealed class V1ImportService(
    AppDatabase database,
    ICelestialRepository catalog,
    ISettingsService settings,
    IObservationPlanService plan,
    IObservationLogService logbook,
    ILocationRepository locations,
    string v1Directory)
{
    /// <summary>Key of the settings table that records that the offer was made.</summary>
    public const string OfferedKey = "v1_import_offered";

    public string V1Directory { get; } = v1Directory;

    public bool ShouldOffer => SettingsTable.Read(database, OfferedKey) is null && V1Importer.HasData(V1Directory);

    public void MarkOffered() => SettingsTable.Write(database, OfferedKey, "true");

    /// <summary>Imports the v1 data and marks the offer as made.</summary>
    public async Task<V1ImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        var objects = await catalog.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var data = V1Importer.Read(V1Directory, objects.ToList());
        var result = V1Importer.Apply(data, settings, plan, logbook, locations);
        MarkOffered();
        return result;
    }

    /// <summary>The summary shown to the user after importing.</summary>
    public static string Describe(V1ImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var summary = DisplayNames.Format(Strings.ImportV1DoneFormat,
            result.SettingsImported ? Strings.Yes : Strings.No, result.PlanItemsAdded, result.LogEntriesAdded);
        return result.Warnings.Count == 0
            ? summary
            : summary + DisplayNames.Format(Strings.ImportV1NotesFormat, string.Join(Environment.NewLine, result.Warnings.Take(10)));
    }

    /// <summary>The v1 data folder of the current user.</summary>
    public static string DefaultV1Directory() =>
        V1Importer.DefaultDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>True for the errors an import can raise (shown to the user, never fatal).</summary>
    public static bool IsImportError(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException;
}
