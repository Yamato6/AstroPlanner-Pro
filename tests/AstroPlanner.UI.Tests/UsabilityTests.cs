using System.Globalization;
using System.Windows.Documents;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>
/// Search, magnitude filter, live mode, My plan (notes, order, CSV, print) and the sky chart.
/// </summary>
public sealed class UsabilityTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private readonly FakeSettingsService _settingsService = new(new ObserverSettings { MinimumAltitude = 20 });
    private readonly FakePlanService _planService = new();

    private ShellViewModel CreateMain(TimeProvider? time = null) =>
        TestShell.Create(plan: _planService, settings: _settingsService, time: time);

    private async Task<ShellViewModel> CreateLoadedMainAsync()
    {
        var viewModel = CreateMain(new ManualTimeProvider(WinterNightUtc));
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    // ─── Search and magnitude ───────────────────────────────────────────

    [Theory]
    [InlineData("M31", "M31", true)]
    [InlineData("m 31", "M31", true)]
    [InlineData("31", "M31", true)]
    [InlineData("M3", "M31", false)]     // the id is exact: M3 does not find M31
    [InlineData("andromeda", "M31", true)]
    [InlineData("ANDRÓMEDA", "M31", true)]   // accents and case are ignored
    [InlineData("orion", "M42", true)]   // finds the name "Orion Nebula" and the constellation "Orion"
    [InlineData("orion nebula", "M42", true)]
    [InlineData("hercules", "M42", false)]
    [InlineData("", "M42", true)]
    [InlineData("NGC 1976", "M42", true)]  // cross-identifications
    [InlineData("ngc1976", "M42", true)]
    [InlineData("NGC 0224", "M31", true)]  // leading zeros are ignored
    [InlineData("ngc 197", "M42", false)]  // ids are exact here too
    [InlineData("great orion", "M42", true)]  // alternate names
    public void CatalogSearch_MatchesIdsCrossIdsNamesAndConstellationIgnoringAccents(string query, string id, bool expected)
    {
        var item = new[] { TestCatalog.M42, TestCatalog.M31, TestCatalog.M13 }.Single(o => o.Id == id);

        CatalogSearch.Matches(new CelestialObjectRowViewModel(item), query).Should().Be(expected);
    }

    [Theory]
    [InlineData("C14", "c14")]
    [InlineData("Sh2-155", "sh2155")]
    [InlineData("IC 0434", "ic434")]
    [InlineData("M101", "m101")]
    [InlineData("PGC 002557", "pgc2557")]
    public void CompactKey_NormalizesIdentifiers(string identifier, string key)
    {
        CatalogSearch.CompactKey(identifier).Should().Be(key);
    }

    [Fact]
    public async Task SearchText_FiltersTheTableAndClearingRestoresIt()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Targets.SearchText = "andrómeda";
        viewModel.Targets.Objects.Select(o => o.Id).Should().Equal("M31");
        viewModel.Targets.SelectedObject!.Id.Should().Be("M31");

        viewModel.Targets.SearchText = "";
        viewModel.Targets.Objects.Should().HaveCount(3);
    }

    [Fact]
    public async Task MaximumMagnitude_HidesFainterObjects()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Targets.MaximumMagnitude = 5.0;

        viewModel.Targets.Objects.Select(o => o.Id).Should().BeEquivalentTo(["M42", "M31"]);
    }

    [Fact]
    public async Task ResetFilters_ClearsSearchAndRestoresSavedMagnitude()
    {
        var viewModel = await CreateLoadedMainAsync();
        viewModel.Targets.SearchText = "m42";
        viewModel.Targets.MaximumMagnitude = 3;

        viewModel.Targets.ResetFiltersCommand.Execute(null);

        viewModel.Targets.SearchText.Should().BeEmpty();
        viewModel.Targets.MaximumMagnitude.Should().Be(12);
        viewModel.Targets.Objects.Should().HaveCount(3);
    }

    // ─── Live mode ──────────────────────────────────────────────────────

    [Fact]
    public void SetNow_UsesTheObserverTimeZoneNotThePcOne()
    {
        var viewModel = CreateMain(new ManualTimeProvider(WinterNightUtc));
        viewModel.ObservationDateTime = new DateTime(2020, 1, 1, 12, 0, 0);

        viewModel.SetNowCommand.Execute(null);

        viewModel.ObservationDateTime.Should().Be(new DateTime(2026, 1, 15, 22, 0, 0));
    }

    [Fact]
    public void LiveMode_FollowsTheClockOnEachTick()
    {
        var clock = new ManualTimeProvider(WinterNightUtc);
        var viewModel = CreateMain(clock);
        viewModel.ObservationDateTime = new DateTime(2026, 1, 10, 20, 0, 0);

        viewModel.IsLive = true;
        viewModel.ObservationDateTime.Should().Be(new DateTime(2026, 1, 15, 22, 0, 0));

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        clock.LastTimer!.Fire();

        viewModel.ObservationDateTime.Should().Be(new DateTime(2026, 1, 15, 22, 1, 0));
        viewModel.IsLive.Should().BeTrue();
    }

    [Fact]
    public void LiveMode_TickFromAnotherThreadIsMarshalledToTheUiContext()
    {
        // Regression: the real timer fires on a pool thread. The change must go through the UI
        // context instead of being applied on that thread (WPF would throw and the app would close).
        var clock = new ManualTimeProvider(WinterNightUtc);
        var viewModel = CreateMain(clock);
        var uiContext = new QueueingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(uiContext);
        try
        {
            viewModel.IsLive = true;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var poolThread = new Thread(() => clock.LastTimer!.Fire());
        poolThread.Start();
        poolThread.Join();

        viewModel.ObservationDateTime.Should().Be(new DateTime(2026, 1, 15, 22, 0, 0), "it has not been applied on the UI thread yet");
        uiContext.Pending.Should().Be(1);

        uiContext.RunPending();

        viewModel.ObservationDateTime.Should().Be(new DateTime(2026, 1, 15, 22, 1, 0));
    }

    /// <summary>Context that queues what it is posted, like the WPF dispatcher.</summary>
    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int Pending
        {
            get { lock (_queue) return _queue.Count; }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue) _queue.Enqueue((d, state));
        }

        public void RunPending()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_queue)
                {
                    if (_queue.Count == 0) return;
                    item = _queue.Dequeue();
                }

                item.Callback(item.State);
            }
        }
    }

    [Fact]
    public void LiveMode_ChoosingATimeByHandTurnsItOff()
    {
        var clock = new ManualTimeProvider(WinterNightUtc);
        var viewModel = CreateMain(clock);
        viewModel.IsLive = true;
        var timer = clock.LastTimer!;

        viewModel.ShiftObservationTimeCommand.Execute("60");

        viewModel.IsLive.Should().BeFalse();
        timer.IsDisposed.Should().BeTrue();
        viewModel.ObservationDateTime.Should().Be(new DateTime(2026, 1, 15, 23, 0, 0));
    }

    // ─── My plan ────────────────────────────────────────────────────────

    [Fact]
    public async Task ObservationPlan_KeepsThePriorityOrder_AndKnowsTheCulminations()
    {
        var viewModel = await CreateLoadedMainAsync();

        // New objects get the lowest priority; the scheduler (with a rig) decides the times.
        foreach (var id in new[] { "M42", "M31" })
        {
            viewModel.Targets.SelectedObject = viewModel.Targets.Objects.Single(o => o.Id == id);
            viewModel.Detail.AddToPlanCommand.Execute(null);
        }

        viewModel.Plan.Items.Select(item => (item.Id, item.Priority)).Should().Equal(("M42", 1), ("M31", 2));
        viewModel.Plan.Items.Should().OnlyContain(item => item.CulminationLocal >= viewModel.Observer.Night!.WindowStartLocal);
    }

    [Fact]
    public async Task ObservationPlan_NotesAreRestoredAndSaved()
    {
        _planService.Entries = [new("M42", "use the UHC filter")];
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Plan.Items.Single().Note.Should().Be("use the UHC filter");

        viewModel.Plan.Items.Single().Note = "25 mm eyepiece";

        _planService.Entries.Should().Equal(new ObservationPlanEntry("M42", "25 mm eyepiece"));
    }

    [Fact]
    public async Task PlanCsv_HasHeaderOneRowPerItemAndEscapesSeparators()
    {
        _planService.Entries = [new("M42", "UHC filter, 25 mm eyepiece"), new("M31")];
        var viewModel = await CreateLoadedMainAsync();

        var lines = viewModel.Plan.BuildCsv(CultureInfo.InvariantCulture)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("Order,ID,Name");
        // Without a rig nothing is scheduled: the plan keeps its priority order.
        lines[1].Should().StartWith("1,M42,").And.EndWith("\"UHC filter, 25 mm eyepiece\"");
        lines[2].Should().StartWith("2,M31,Andromeda Galaxy");
    }

    [Fact]
    public async Task PlanCsv_UsesTheCultureListSeparator()
    {
        _planService.Entries = [new("M42")];
        var viewModel = await CreateLoadedMainAsync();

        var csv = viewModel.Plan.BuildCsv(CultureInfo.GetCultureInfo("es-ES"));

        csv.Should().StartWith("Order;ID;Name").And.Contain(";4,0;", "es-ES uses a decimal comma and a semicolon separator");
    }

    [Fact]
    public async Task ExportObservationPlan_WritesUtf8FileWithBom()
    {
        _planService.Entries = [new("M31")];
        var viewModel = await CreateLoadedMainAsync();
        var path = Path.Combine(Path.GetTempPath(), $"astroplanner-plan-{Guid.NewGuid():N}.csv");

        try
        {
            viewModel.Plan.Export(path).Should().BeTrue();

            var bytes = File.ReadAllBytes(path);
            bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
            File.ReadAllText(path).Should().Contain("Andromeda Galaxy");
            viewModel.Plan.StatusMessage.Should().StartWith("Plan exported");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PrintDocument_HasHeaderRowPlusOneRowPerItem()
    {
        _planService.Entries = [new("M42", "note"), new("M31")];
        var viewModel = await CreateLoadedMainAsync();
        var items = viewModel.Plan.Items.ToList();
        var summary = viewModel.Plan.SummaryText;

        // FlowDocument needs an STA thread, like the UI one.
        var rowCount = RunOnStaThread(() =>
        {
            var document = PlanPrintDocument.Create(items, summary);
            return document.Blocks.OfType<Table>().Single().RowGroups[0].Rows.Count;
        });

        rowCount.Should().Be(3);
        summary.Should().Contain("Night of 2026-01-15").And.Contain("Moon");
    }

    // ─── Sky chart ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(90, 0, 0, 0)]      // zenith at the center
    [InlineData(0, 0, 0, 90)]      // north up, on the horizon
    [InlineData(0, 90, -90, 0)]    // east to the left
    [InlineData(0, 270, 90, 0)]    // west to the right
    [InlineData(30, 180, 0, -60)]  // south, 30° high
    public void SkyProjection_PutsZenithInTheCenterAndNorthUp(double altitude, double azimuth, double x, double y)
    {
        var (px, py) = SkyProjection.ToXY(altitude, azimuth);

        px.Should().BeApproximately(x, 1e-9);
        py.Should().BeApproximately(y, 1e-9);
    }

    [Fact]
    public async Task SkyMap_ShowsOnlyObjectsAboveTheHorizonAndMarksPlanAndSelection()
    {
        _planService.Entries = [new("M31")];
        var viewModel = await CreateLoadedMainAsync();

        var points = viewModel.Sky.GetSkyMapPoints();

        points.Should().OnlyContain(p => p.AltitudeDegrees > 0);
        points.Select(p => p.Id).Should().NotContain("M13", "M13 is below the horizon that night");
        points.Single(p => p.Id == "M31").IsInPlan.Should().BeTrue();
        points.Should().ContainSingle(p => p.IsSelected);
    }

    [Fact]
    public async Task SkyMap_ClickSelectsTheNearestObjectOrNothing()
    {
        var viewModel = await CreateLoadedMainAsync();
        var m31 = viewModel.Sky.GetSkyMapPoints().Single(p => p.Id == "M31");

        viewModel.Sky.SelectNearestOnSkyMap(m31.Position.X + 1, m31.Position.Y - 1).Should().BeTrue();
        viewModel.Targets.SelectedObject!.Id.Should().Be("M31");

        viewModel.Sky.SelectNearestOnSkyMap(500, 500).Should().BeFalse();
        viewModel.Targets.SelectedObject!.Id.Should().Be("M31");
    }

    private static T RunOnStaThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("Failed on the STA thread.", error);
    }
}
