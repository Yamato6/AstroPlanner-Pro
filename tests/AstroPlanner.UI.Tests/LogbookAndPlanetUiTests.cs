using System.Globalization;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>
/// Observation logbook (persistence, Messier progress, CSV, table marks) and planets in the planner.
/// </summary>
public sealed class LogbookAndPlanetUiTests : IDisposable
{
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private readonly FakeLogService _logService = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public LogbookAndPlanetUiTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private LogbookViewModel CreateLogbook() => new(_logService, new NullLogger());

    private static ObservationLogEntry Entry(string id, DateTime when, int rating = 4) =>
        new(Guid.NewGuid(), id, id, when, "CDMX", "Dobson 8\"", 3, 4, rating, "");

    private static async Task<ShellViewModel> CreateLoadedMainAsync(LogbookViewModel logbook, params CelestialObject[] extra)
    {
        var viewModel = TestShell.Create(
            [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13, .. extra],
            logbook: logbook,
            time: new ManualTimeProvider(WinterNightUtc));
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    // ─── Logbook ────────────────────────────────────────────────────────

    [Fact]
    public void Add_StoresTheEntryNewestFirstAndRemembersTheEquipment()
    {
        var logbook = CreateLogbook();

        var first = logbook.CreateDraft("M42", "Orion Nebula", new DateTime(2026, 1, 10, 22, 15, 0), "CDMX");
        first.Equipment = "Refractor 80 mm";
        first.Rating = 5;
        logbook.Add(first).Should().BeTrue();

        var second = logbook.CreateDraft("M31", "Andromeda Galaxy", new DateTime(2026, 1, 12, 21, 0, 0), "CDMX");
        second.Equipment.Should().Be("Refractor 80 mm", "the equipment of the last observation is suggested");
        logbook.Add(second).Should().BeTrue();

        logbook.Entries.Select(e => e.ObjectId).Should().Equal("M31", "M42");
        logbook.Entries.Last().ObservedAtLocal.Should().Be(new DateTime(2026, 1, 10, 22, 15, 0));
        logbook.Entries.Last().RatingStars.Should().Be("★★★★★");
        _logService.Entries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("22:30", true)]
    [InlineData("7:05", true)]
    [InlineData("24:00", false)]
    [InlineData("22.30", false)]
    [InlineData("", false)]
    public void Draft_RequiresAValidTime(string time, bool valid)
    {
        var logbook = CreateLogbook();
        var draft = logbook.CreateDraft("M42", "Orion Nebula", new DateTime(2026, 1, 10, 22, 0, 0), "CDMX");

        draft.ObservedTime = time;

        logbook.Add(draft).Should().Be(valid);
        logbook.Entries.Should().HaveCount(valid ? 1 : 0);
    }

    [Fact]
    public void MessierProgress_CountsDistinctMessierObjectsOnly()
    {
        _logService.Entries =
        [
            Entry("M42", new DateTime(2026, 1, 10)),
            Entry("M42", new DateTime(2026, 1, 11)),   // repeated: counts once
            Entry("M31", new DateTime(2026, 1, 11)),
            Entry("Jupiter", new DateTime(2026, 1, 11)) // not a Messier object
        ];

        var logbook = CreateLogbook();

        logbook.ObservedMessierCount.Should().Be(2);
        logbook.MessierProgressText.Should().Be("2 / 110");
        logbook.HasBeenObserved("Jupiter").Should().BeTrue();
    }

    [Fact]
    public void Delete_RemovesTheEntryAndUpdatesProgress()
    {
        _logService.Entries = [Entry("M42", new DateTime(2026, 1, 10))];
        var logbook = CreateLogbook();

        logbook.DeleteCommand.Execute(logbook.Entries[0]);

        logbook.Entries.Should().BeEmpty();
        logbook.ObservedMessierCount.Should().Be(0);
        _logService.Entries.Should().BeEmpty();
    }

    [Fact]
    public void SaveFailure_KeepsMemoryConsistentWithDiskAndReportsIt()
    {
        var logbook = CreateLogbook();
        _logService.FailOnSave = true;

        var added = logbook.Add(logbook.CreateDraft("M42", "Orion Nebula", new DateTime(2026, 1, 10, 22, 0, 0), "CDMX"));

        added.Should().BeFalse();
        logbook.Entries.Should().BeEmpty("what could not be saved must not stay half-done on screen");
        logbook.StatusMessage.Should().Contain("Could not save");
    }

    [Fact]
    public void Csv_ListsEntriesOldestFirstAndEscapesNotes()
    {
        _logService.Entries =
        [
            Entry("M31", new DateTime(2026, 1, 12, 21, 0, 0)) with { Notes = "arms visible, no filter" },
            Entry("M42", new DateTime(2026, 1, 10, 22, 15, 0))
        ];
        var logbook = CreateLogbook();

        var lines = logbook.BuildCsv(CultureInfo.InvariantCulture)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("Date,Time,ID,Name");
        lines[1].Should().StartWith("2026-01-10,22:15,M42");
        lines[2].Should().EndWith("\"arms visible, no filter\"");
    }

    [Fact]
    public async Task ObservedObjects_AreMarkedInTheTableAndUpdateLive()
    {
        _logService.Entries = [Entry("M42", new DateTime(2026, 1, 10))];
        var logbook = CreateLogbook();
        var viewModel = await CreateLoadedMainAsync(logbook);

        viewModel.Targets.Objects.Single(o => o.Id == "M42").IsObserved.Should().BeTrue();
        viewModel.Targets.Objects.Single(o => o.Id == "M31").IsObserved.Should().BeFalse();
        viewModel.Tonight.ObservedSummaryText.Should().Be("Messier observed: 1 / 110");

        viewModel.Targets.SelectedObject = viewModel.Targets.Objects.Single(o => o.Id == "M31");
        var draft = viewModel.Detail.CreateLogDraft()!;
        draft.ObjectId.Should().Be("M31");
        draft.ObservedAtLocal.Should().Be(new DateTime(2026, 1, 15, 22, 0, 0));
        logbook.Add(draft);

        viewModel.Targets.Objects.Single(o => o.Id == "M31").IsObserved.Should().BeTrue();
        viewModel.Tonight.ObservedSummaryText.Should().Be("Messier observed: 2 / 110");
    }

    // ─── Planets in the planner ─────────────────────────────────────────

    [Fact]
    public async Task Planets_GetDateDependentMagnitudeAndPlanetDetails()
    {
        var jupiter = SolarSystemCatalog.Planets.Single(p => p.Planet == Planet.Jupiter);
        var viewModel = await CreateLoadedMainAsync(CreateLogbook(), jupiter);

        var row = viewModel.Targets.Objects.Single(o => o.Id == "Jupiter");
        row.IsPlanet.Should().BeTrue();
        row.ApparentMagnitude.Should().BeApproximately(-2.7, 0.3, "Jupiter was at opposition on 2026-01-10");
        row.MoonInterferes.Should().BeFalse("the Moon does not hide a bright planet");

        viewModel.Targets.SelectedObject = row;
        var detail = viewModel.Detail.Observation!;
        detail.IsPlanet.Should().BeTrue();
        detail.Magnitude.Should().BeApproximately(row.ApparentMagnitude, 0.01);
        detail.Planet!.ApparentDiameterArcsec.Should().BeGreaterThan(40);

        viewModel.Targets.TypeFilter = nameof(CelestialObjectType.Planet);
        viewModel.Targets.Objects.Should().ContainSingle().Which.Id.Should().Be("Jupiter");

        viewModel.Targets.TypeFilter = "All";
        viewModel.Targets.SearchText = "jupiter";
        viewModel.Targets.Objects.Should().ContainSingle().Which.Id.Should().Be("Jupiter");

        viewModel.Targets.SearchText = "";
        viewModel.Sky.GetSkyMapPoints().Single(p => p.Id == "Jupiter").IsPlanet.Should().BeTrue();
    }
}
