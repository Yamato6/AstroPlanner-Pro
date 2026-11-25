using System.Text.Json;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Data.Storage;

/// <summary>
/// The planetarium UI's window layout (table <c>window_layouts</c>, migration 0006) and its
/// preferences (JSON under the "ui" key of the settings table).
/// </summary>
public sealed class SqliteInterfaceStore(AppDatabase database) : IInterfaceStore
{
    private const string PreferencesKey = "ui";
    private readonly AppDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public IReadOnlyList<WindowLayout> LoadLayouts()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT window_id, is_open, is_minimized, arrangement, x, y, width, height, dock_width, z_order
            FROM window_layouts ORDER BY z_order, window_id
            """;
        using var reader = command.ExecuteReader();
        var layouts = new List<WindowLayout>();
        while (reader.Read())
        {
            // A name this version does not know (written by a newer one) falls back to floating.
            var arrangement = Enum.TryParse<WindowArrangement>(reader.GetString(3), out var parsed) ? parsed : WindowArrangement.Floating;
            layouts.Add(new WindowLayout(
                reader.GetString(0),
                reader.GetInt64(1) != 0,
                reader.GetInt64(2) != 0,
                arrangement,
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9)));
        }

        return layouts;
    }

    public void SaveLayouts(IReadOnlyList<WindowLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        Sql.Execute(connection, transaction, "DELETE FROM window_layouts");
        using var insert = Sql.Command(connection, transaction,
            """
            INSERT INTO window_layouts (window_id, is_open, is_minimized, arrangement, x, y, width, height, dock_width, z_order)
            VALUES ($id, $open, $minimized, $arrangement, $x, $y, $width, $height, $dock, $z)
            """,
            "$id", "$open", "$minimized", "$arrangement", "$x", "$y", "$width", "$height", "$dock", "$z");
        // One row per window: the last of duplicates wins.
        foreach (var layout in layouts.Where(l => !string.IsNullOrWhiteSpace(l.Id)).GroupBy(l => l.Id).Select(g => g.Last()))
        {
            Sql.Run(insert, layout.Id, layout.IsOpen ? 1 : 0, layout.IsMinimized ? 1 : 0, layout.Arrangement.ToString(),
                layout.X, layout.Y, layout.Width, layout.Height, layout.DockWidth, layout.ZOrder);
        }

        transaction.Commit();
    }

    public InterfacePreferences LoadPreferences()
    {
        try
        {
            return SettingsTable.Read(_database, PreferencesKey) is { } json
                ? JsonSerializer.Deserialize<InterfacePreferences>(json) ?? new InterfacePreferences()
                : new InterfacePreferences();
        }
        catch (JsonException)
        {
            return new InterfacePreferences();
        }
    }

    public void SavePreferences(InterfacePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        SettingsTable.Write(_database, PreferencesKey, JsonSerializer.Serialize(preferences));
    }
}
