using AstroPlanner.Core.Models;
using AstroPlanner.Data.Storage;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.Data.Tests;

/// <summary>Each test gets its own folder under %TEMP%; the real %LocalAppData% is never touched.</summary>
public abstract class TempFolderTest : IDisposable
{
    protected string Folder { get; } = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    protected string DatabasePath => Path.Combine(Folder, "app.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

public sealed class AppDatabaseTests : TempFolderTest
{
    [Fact]
    public void Initialize_CreatesTheDatabaseAtTheLatestVersion()
    {
        var database = new AppDatabase(DatabasePath);

        database.Initialize();

        database.WasCreated.Should().BeTrue();
        database.SchemaVersion.Should().Be(database.LatestVersion).And.BeGreaterThanOrEqualTo(1);
        File.Exists(DatabasePath).Should().BeTrue();
    }

    [Fact]
    public void Initialize_AnExistingDatabase_IsNotNew_AndNotBackedUpWhenUpToDate()
    {
        new AppDatabase(DatabasePath).Initialize();

        var again = new AppDatabase(DatabasePath);
        again.Initialize();

        again.WasCreated.Should().BeFalse();
        again.BackupFilePath.Should().BeNull();
    }

    [Fact]
    public void NewMigration_BacksUpTheDatabaseFirst_AndKeepsTheData()
    {
        var v1 = new[] { (1, "0001_a.sql", "CREATE TABLE t (x INTEGER);"), };
        var v2 = v1.Append((2, "0002_b.sql", "ALTER TABLE t ADD COLUMN y TEXT;")).ToArray();
        var first = new AppDatabase(DatabasePath, null, v1);
        using (var connection = first.Open())
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO t (x) VALUES (42)";
            insert.ExecuteNonQuery();
        }

        var upgraded = new AppDatabase(DatabasePath, null, v2);
        upgraded.Initialize();

        upgraded.SchemaVersion.Should().Be(2);
        upgraded.BackupFilePath.Should().EndWith("app.backup-v1.db");
        File.Exists(upgraded.BackupFilePath).Should().BeTrue();
        using var check = upgraded.Open();
        using var query = check.CreateCommand();
        query.CommandText = "SELECT x FROM t";
        query.ExecuteScalar().Should().Be(42L);
    }

    [Fact]
    public void FailingMigration_IsRolledBack()
    {
        var migrations = new[] { (1, "0001_a.sql", "CREATE TABLE t (x INTEGER);"), (2, "0002_bad.sql", "CREATE TABLE u (y INTEGER); THIS IS NOT SQL;") };
        var database = new AppDatabase(DatabasePath, null, migrations);

        var act = database.Initialize;

        act.Should().Throw<SqliteException>();
        using var connection = new AppDatabase(DatabasePath, null, migrations.Take(1)).Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'u'";
        command.ExecuteScalar().Should().Be(0L);
    }

    [Fact]
    public void DatabaseFromANewerVersion_IsRejected()
    {
        new AppDatabase(DatabasePath, null, [(99, "0099_future.sql", "SELECT 1;")]).Initialize();

        var act = new AppDatabase(DatabasePath).Initialize;

        act.Should().Throw<InvalidDataException>().WithMessage("*newer version*");
    }

    [Fact]
    public void CorruptFile_IsSetAside_AndANewDatabaseCreated()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(DatabasePath, "this is not a database, but it is long enough to have a header............");
        var database = new AppDatabase(DatabasePath);

        database.Initialize();

        database.WasCreated.Should().BeTrue();
        database.PreservedCorruptFilePath.Should().NotBeNull();
        File.ReadAllText(database.PreservedCorruptFilePath!).Should().StartWith("this is not a database");
        new SqliteSettingsService(database).Load().MinimumAltitude.Should().Be(20);
    }
}

public sealed class SqliteStoreTests : TempFolderTest
{
    private AppDatabase Database() => new(DatabasePath);

    [Fact]
    public void Settings_DefaultsUntilSaved_ThenRoundTrip()
    {
        var service = new SqliteSettingsService(Database());
        service.Load().TimeZoneId.Should().Be("Central Standard Time (Mexico)");

        service.Save(new ObserverSettings
        {
            CityId = null, IsManualLocation = true, LatitudeDegrees = 21.88, LongitudeDegrees = -102.29, ElevationMeters = 1888,
            TimeZoneId = "Central Standard Time (Mexico)", ActiveLocationId = "home", MinimumAltitude = 25,
            MaximumMagnitude = 11, WeatherForecastEnabled = false, CheckForUpdates = false
        });
        var loaded = new SqliteSettingsService(Database()).Load();

        loaded.Should().BeEquivalentTo(new
        {
            CityId = (string?)null, IsManualLocation = true, LatitudeDegrees = 21.88, ActiveLocationId = "home",
            MinimumAltitude = 25.0, WeatherForecastEnabled = false, CheckForUpdates = false
        });
    }

    [Fact]
    public void Settings_DamagedValue_FallsBackToDefaults()
    {
        var database = Database();
        SettingsTable.Write(database, "observer", "{ not json");

        new SqliteSettingsService(database).Load().MinimumAltitude.Should().Be(20);
    }

    [Fact]
    public void SkyView_DefaultsUntilSaved_ThenRoundTrip_AndSurvivesDamage()
    {
        var database = Database();
        var store = new SqliteSkyViewStore(database);
        store.Load().StarLimitMagnitude.Should().Be(8.5);

        store.Save(new SkyViewSettings { EquatorialGrid = true, StarLimitMagnitude = 7, FovDegrees = 30, Ground = false });
        var loaded = new SqliteSkyViewStore(Database()).Load();
        loaded.Should().BeEquivalentTo(new { EquatorialGrid = true, StarLimitMagnitude = 7.0, FovDegrees = 30.0, Ground = false, Stars = true });

        SettingsTable.Write(database, "sky", "[broken");
        store.Load().FovDegrees.Should().Be(70);
    }

    [Fact]
    public void WindowLayouts_AreEmptyUntilSaved_ThenSaveReplacesThem()
    {
        var store = new SqliteInterfaceStore(Database());
        store.LoadLayouts().Should().BeEmpty();

        store.SaveLayouts(
        [
            new WindowLayout("settings", true, false, WindowArrangement.DockRight, 120, 80, 420, 560, 380, 3),
            new WindowLayout("help", false, false, WindowArrangement.Floating, -40, 60, 520, 600, 360, 1),
        ]);
        store.SaveLayouts(
        [
            new WindowLayout("help", true, true, WindowArrangement.Maximized, -40, 60, 520, 600, 360, 2),
            new WindowLayout("help", true, false, WindowArrangement.Floating, 10, 20, 500, 400, 360, 4),
        ]);

        // In z-order; the last of two rows for one window wins.
        new SqliteInterfaceStore(Database()).LoadLayouts().Should().Equal(
            new WindowLayout("help", true, false, WindowArrangement.Floating, 10, 20, 500, 400, 360, 4));
    }

    [Fact]
    public void WindowLayouts_WithAnArrangementFromANewerVersion_FloatInstead()
    {
        var database = Database();
        new SqliteInterfaceStore(database).SaveLayouts([new WindowLayout("help", true, false, WindowArrangement.DockLeft, 0, 0, 400, 300, 360, 1)]);
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE window_layouts SET arrangement = 'Tabbed'";
            command.ExecuteNonQuery();
        }

        new SqliteInterfaceStore(database).LoadLayouts().Single().Arrangement.Should().Be(WindowArrangement.Floating);
    }

    [Fact]
    public void InterfacePreferences_DefaultUntilSaved_ThenRoundTrip_AndSurviveDamage()
    {
        var database = Database();
        var store = new SqliteInterfaceStore(database);
        store.LoadPreferences().ReduceTransparency.Should().BeFalse();

        store.SavePreferences(new InterfacePreferences { ReduceTransparency = true });
        new SqliteInterfaceStore(Database()).LoadPreferences().ReduceTransparency.Should().BeTrue();

        SettingsTable.Write(database, "ui", "{ broken");
        store.LoadPreferences().Should().Be(new InterfacePreferences());
    }

    [Fact]
    public void Plan_KeepsOrderAndNotes_AndSaveReplacesIt()
    {
        var service = new SqlitePlanService(Database());

        service.Save([new("M42", "first"), new("Jupiter"), new("NGC 7000", "wide")]);
        service.Load().Should().Equal(new ObservationPlanEntry("M42", "first"), new("Jupiter"), new("NGC 7000", "wide"));

        service.Save([new("M31")]);
        new SqlitePlanService(Database()).Load().Should().Equal(new ObservationPlanEntry("M31"));
    }

    [Fact]
    public void Plan_KeepsLockedBlocks()
    {
        var service = new SqlitePlanService(Database());
        var locked = new ObservationPlanEntry("M42", "after midnight")
        {
            Locked = true,
            StartLocal = new DateTime(2026, 12, 10, 0, 30, 0),
            EndLocal = new DateTime(2026, 12, 10, 2, 45, 0)
        };

        service.Save([locked, new("M31")]);

        new SqlitePlanService(Database()).Load().Should().Equal(locked, new ObservationPlanEntry("M31"));
    }

    [Fact]
    public void Checklist_RoundTrips()
    {
        var service = new SqlitePlanService(Database());
        service.LoadChecklist().Should().BeEmpty();

        service.SaveChecklist(["Batteries", "Focus", " "]);
        new SqlitePlanService(Database()).LoadChecklist().Should().Equal("Batteries", "Focus");

        service.SaveChecklist([]);
        service.LoadChecklist().Should().BeEmpty();
    }

    [Fact]
    public void Logbook_RoundTripsEveryField()
    {
        var entry = new ObservationLogEntry(Guid.NewGuid(), "M42", "Orion Nebula", new DateTime(2026, 1, 15, 22, 30, 15),
            "Home", "T7 + 16 mm", 3, 4, 5, "First photo");
        var service = new SqliteLogbookService(Database());

        service.Save([entry, entry]);

        new SqliteLogbookService(Database()).Load().Should().Equal(entry);
    }

    [Fact]
    public void Logbook_RoundTripsPhotographedEntries_WithTheirCaptureAndPrediction()
    {
        var photographed = new ObservationLogEntry(Guid.NewGuid(), "M42", "Orion Nebula", new DateTime(2026, 1, 15, 22, 30, 0),
            "Home", "Rokinon 135", 3, 4, 4, "")
        {
            Kind = LogEntryKind.Photographed,
            Capture = new CaptureDetails { RigId = "rig-1", RigName = "Rokinon 135", SubSeconds = 30.5, Iso = 800, SubCount = 120, ImagePath = @"C:\Astro\m42.tif" },
            Prediction = new PredictionSummary(Core.Imaging.Feasibility.FeasibilityVerdict.Good, 64, 18.5) { SubSeconds = 30, SubCount = 110 }
        };
        var bare = new ObservationLogEntry(Guid.NewGuid(), "M31", "Andromeda", new DateTime(2026, 1, 16, 21, 0, 0), "", "", 3, 3, 3, "")
        {
            Kind = LogEntryKind.Photographed,
            Capture = new CaptureDetails()
        };
        var observed = new ObservationLogEntry(Guid.NewGuid(), "M13", "Hercules", new DateTime(2026, 1, 17, 5, 0, 0), "", "", 3, 3, 3, "")
        {
            Capture = new CaptureDetails { RigName = "ignored" },
            Prediction = new PredictionSummary(Core.Imaging.Feasibility.FeasibilityVerdict.Good, 50, 10)
        };
        var service = new SqliteLogbookService(Database());

        service.Save([photographed, bare, observed]);

        var loaded = new SqliteLogbookService(Database()).Load();
        loaded.Should().HaveCount(3);
        loaded[0].Should().Be(photographed);
        loaded[1].Should().Be(bare);
        loaded[2].Capture.Should().BeNull("an observed entry has no capture");
        loaded[2].Prediction.Should().Be(observed.Prediction);
    }

    [Fact]
    public void Logbook_IgnoresAPredictionItCannotRead()
    {
        var database = Database();
        var entry = new ObservationLogEntry(Guid.NewGuid(), "M42", "Orion Nebula", new DateTime(2026, 1, 15, 22, 30, 0), "", "", 3, 3, 3, "");
        new SqliteLogbookService(database).Save([entry]);
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE logbook SET prediction = '{not json', kind = 'Unknown'";
            command.ExecuteNonQuery();
        }

        var loaded = new SqliteLogbookService(database).Load().Single();

        loaded.Prediction.Should().BeNull();
        loaded.Kind.Should().Be(LogEntryKind.Observed);
    }

    [Fact]
    public void Locations_SaveUpdateAndDelete_KeepTheOrder()
    {
        var repository = new SqliteLocationRepository(Database());
        var mask = new HorizonMask([new HorizonPoint(90, 15), new HorizonPoint(270, 5)]);
        var home = new Location("home", "Home", 21.88, -102.29, 1888, "America/Mexico_City", "Central Standard Time (Mexico)", mask, "Roof");
        var dark = new Location("dark", "Dark site", 22.1, -102.6, 2100, "America/Mexico_City", "Central Standard Time (Mexico)", zenithSqm: 21.4);

        repository.Save(home);
        repository.Save(dark);
        repository.Save(home with { Name = "Home (roof)" });

        var all = new SqliteLocationRepository(Database()).GetAll();
        all.Select(l => l.Name).Should().Equal("Home (roof)", "Dark site");
        all[0].HorizonMask.Should().Be(mask);
        all[0].Notes.Should().Be("Roof");
        all[0].ZenithSqm.Should().BeNull();
        all[1].ZenithSqm.Should().Be(21.4);

        repository.Delete("home");
        repository.Delete("missing");
        repository.GetAll().Select(l => l.Id).Should().Equal("dark");
    }

    [Fact]
    public void HorizonMaskJson_RoundTrips()
    {
        var mask = new HorizonMask([new HorizonPoint(0, 10), new HorizonPoint(180, 20.5)]);

        HorizonMaskJson.Parse(HorizonMaskJson.Serialize(mask)).Should().Be(mask);
        HorizonMaskJson.Parse("").Should().Be(HorizonMask.Flat);
        HorizonMaskJson.Serialize(mask).Should().Be("""[{"az":0,"alt":10},{"az":180,"alt":20.5}]""");
    }
}
