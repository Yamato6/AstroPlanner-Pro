using System.Globalization;
using System.Text;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.ViewModels;
using PdfSharp.Pdf.IO;

namespace AstroPlanner.UI.Tests;

/// <summary>M3b: session planner (schedule, priorities, locks, checklist) and its exports.</summary>
public sealed class SessionPlannerTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Aguascalientes (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private static readonly ObserverSettings Aguascalientes = new()
    {
        CityId = null,
        IsManualLocation = true,
        LatitudeDegrees = 21.88,
        LongitudeDegrees = -102.29,
        ElevationMeters = 1888,
        TimeZoneId = "Central Standard Time (Mexico)",
        MinimumAltitude = 20,
        MaximumMagnitude = 20
    };

    private static readonly IReadOnlyList<CelestialObject> Catalog =
        [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13, .. SolarSystemCatalog.Planets];

    private static async Task<ShellViewModel> CreateAsync(FakePlanService plan, EquipmentInventory? inventory = null)
    {
        var shell = TestShell.Create(
            catalog: Catalog,
            plan: plan,
            settings: new FakeSettingsService(Aguascalientes),
            time: new ManualTimeProvider(WinterNightUtc),
            equipment: new FakeEquipmentRepository(inventory ?? EquipmentInventory.Demo),
            atlas: new FakeAtlas(1.84));
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        await shell.Targets.FeasibilityTask;
        return shell;
    }

    private static PlanItemViewModel Item(ShellViewModel shell, string id) => shell.Plan.Items.Single(i => i.Id == id);

    [Fact]
    public async Task Plan_IsScheduledAcrossTheNight_WithoutOverlaps()
    {
        var shell = await CreateAsync(new FakePlanService { Entries = [new("M42"), new("M31")] });

        var items = shell.Plan.Items;
        items.Should().OnlyContain(i => i.HasBlock && i.Feasibility != null);
        var m31 = Item(shell, "M31");
        var m42 = Item(shell, "M42");
        m31.StartLocal.Should().BeBefore(m42.StartLocal!.Value, "Andromeda sets in the evening, Orion is up all night");
        (m31.EndLocal <= m42.StartLocal || m42.EndLocal <= m31.StartLocal).Should().BeTrue();
        m42.SubsText.Should().Contain("ISO 800");
        m42.IntegrationText.Should().NotBeEmpty();
        m42.MeridianFlipLocal.Should().BeNull("the demo rig is on a fixed tripod");
        m42.StatusText.Should().BeEmpty();

        shell.Plan.Timeline.Should().NotBeNull();
        shell.Plan.Timeline!.Rows.Select(r => r.Label).Should().Equal("M42", "M31");
        shell.Plan.Timeline.TwilightBands.Should().HaveCount(2);
        shell.Plan.ScheduleSummary.Should().MatchRegex(@"^Dark time \d+h \d\dm · scheduled \d+h \d\dm · free \d+h \d\dm$");
        shell.Plan.SummaryText.Should().Contain("Rokinon");
        shell.Plan.ChronologicalItems.Select(i => i.Id).Should().Equal("M31", "M42");
    }

    [Fact]
    public async Task WithoutARig_NothingIsScheduled_AndThePlanSaysWhy()
    {
        var shell = await CreateAsync(new FakePlanService { Entries = [new("M42")] }, EquipmentInventory.Empty);

        shell.Plan.Timeline.Should().BeNull();
        shell.Plan.ScheduleSummary.Should().Be(Strings.PlanScheduleNoRig);
        var m42 = Item(shell, "M42");
        m42.HasBlock.Should().BeFalse();
        m42.CulminationLocal.Should().NotBeNull();
    }

    [Fact]
    public async Task MovingAnItem_ChangesItsPriority_AndIsSaved()
    {
        var plan = new FakePlanService { Entries = [new("M42"), new("M31")] };
        var shell = await CreateAsync(plan);

        shell.Plan.MoveUpCommand.Execute(Item(shell, "M31"));

        shell.Plan.Items.Select(i => (i.Id, i.Priority)).Should().Equal(("M31", 1), ("M42", 2));
        plan.Ids.Should().Equal("M31", "M42");

        shell.Plan.MoveDownCommand.Execute(Item(shell, "M42"));   // already last
        plan.Ids.Should().Equal("M31", "M42");
    }

    [Fact]
    public async Task LockingABlock_KeepsIt_AndIsSaved()
    {
        var plan = new FakePlanService { Entries = [new("M42"), new("M31")] };
        var shell = await CreateAsync(plan);
        var m31 = Item(shell, "M31");
        var (start, end) = (m31.StartLocal, m31.EndLocal);

        m31.IsLocked = true;

        m31.Schedule!.Status.Should().Be(Core.Planning.ScheduleStatus.Locked);
        m31.StatusText.Should().Be(Strings.PlanStatusLocked);
        plan.Entries.Single(e => e.Id == "M31").Should().BeEquivalentTo(new { Locked = true, StartLocal = start, EndLocal = end });

        // Even with the lowest priority and after a recalculation, the block stays.
        shell.Plan.MoveUpCommand.Execute(Item(shell, "M31"));
        m31.StartLocal.Should().Be(start);
        m31.EndLocal.Should().Be(end);

        m31.IsLocked = false;
        plan.Entries.Single(e => e.Id == "M31").Should().BeEquivalentTo(new { Locked = false, StartLocal = (DateTime?)null });
    }

    [Fact]
    public async Task ABlockLockedOnAnotherNight_IsReleased()
    {
        var plan = new FakePlanService
        {
            Entries =
            [
                new("M42") { Locked = true, StartLocal = new DateTime(2025, 3, 1, 21, 0, 0), EndLocal = new DateTime(2025, 3, 1, 23, 0, 0) }
            ]
        };

        var shell = await CreateAsync(plan);

        Item(shell, "M42").IsLocked.Should().BeFalse();
        Item(shell, "M42").Schedule!.Status.Should().Be(Core.Planning.ScheduleStatus.Scheduled);
        plan.Entries.Single().Locked.Should().BeFalse();
    }

    [Fact]
    public async Task AutoPrioritize_PutsTheShortestVisibleTargetsFirst()
    {
        var plan = new FakePlanService { Entries = [new("M42"), new("M31")] };
        var shell = await CreateAsync(plan);

        shell.Plan.AutoPrioritizeCommand.Execute(null);

        var hours = shell.Plan.Items.Select(i => i.Feasibility!.GeometricHours).ToList();
        hours.Should().BeInAscendingOrder();
        shell.Plan.Items.Select(i => i.Priority).Should().Equal(1, 2);
        plan.Ids.Should().Equal(shell.Plan.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Checklist_IsSaved_RestoredAndReset()
    {
        var plan = new FakePlanService { Checklist = ["Focus"] };
        var shell = await CreateAsync(plan);

        shell.Plan.Checklist.Single(c => c.Key == "Focus").IsDone.Should().BeTrue();
        shell.Plan.Checklist.Single(c => c.Key == "Batteries").IsDone = true;
        plan.Checklist.Should().BeEquivalentTo("Batteries", "Focus");

        shell.Plan.ResetChecklistCommand.Execute(null);
        plan.Checklist.Should().BeEmpty();
        shell.Plan.Checklist.Should().OnlyContain(c => !c.IsDone);
    }

    [Fact]
    public async Task Csv_HasTheScheduleColumns_InObservingOrder()
    {
        var shell = await CreateAsync(new FakePlanService { Entries = [new("M42"), new("M31")] });

        var lines = shell.Plan.BuildCsv(CultureInfo.InvariantCulture).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().StartWith("Order,ID,Name,Constellation,Type,Magnitude,Start,End,Subs,Sub (s),ISO/gain,Integration (min)");
        lines[1].Should().StartWith("1,M31,");
        lines[2].Should().StartWith("2,M42,").And.Contain(",800,");
    }

    [Fact]
    public async Task Ics_IsAValidCalendar_WithOneEventPerBlock()
    {
        var shell = await CreateAsync(new FakePlanService { Entries = [new("M42", "UHC, 25 mm; check focus\nthen flats"), new("M31")] });

        var ics = shell.Plan.BuildIcs();

        ics.Should().StartWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:").And.EndWith("END:VCALENDAR\r\n");
        ics.Replace("\r\n", "", StringComparison.Ordinal).Should().NotContain("\n", "every line ends in CRLF");
        var lines = ics.Split("\r\n");
        lines.Should().OnlyContain(l => Encoding.UTF8.GetByteCount(l) <= 75);
        var unfolded = ics.Replace("\r\n ", "", StringComparison.Ordinal);
        unfolded.Split("BEGIN:VEVENT").Should().HaveCount(3);
        unfolded.Should().Contain(@"UHC\, 25 mm\; check focus\nthen flats");
        unfolded.Should().Contain("SUMMARY:Image M42 — Orion Nebula");
        unfolded.Should().Contain("DTSTAMP:20260116T040000Z");
        unfolded.Should().MatchRegex(@"DTSTART:2026011[56]T\d{6}Z");
        unfolded.Should().Contain("UID:").And.Contain("-m42@astroplanner-pro");
        unfolded.Should().NotContain("21.88", "coordinates never leave the app");
    }

    [Fact]
    public void IcsEscaping_FollowsRfc5545()
    {
        Services.PlanCalendarExport.Escape(@"a\b;c,d" + "\r\ne").Should().Be(@"a\\b\;c\,d\ne");
    }

    [Fact]
    public async Task Pdf_IsWritten_AndPaginates()
    {
        var shell = await CreateAsync(new FakePlanService
        {
            Entries = [.. Catalog.Select(o => new ObservationPlanEntry(o.Id, "a note that is rather long to see that it gets shortened in the table"))]
        });
        shell.Plan.Checklist[0].IsDone = true;
        var path = Path.Combine(Path.GetTempPath(), $"astroplanner-plan-{Guid.NewGuid():N}.pdf");

        try
        {
            shell.Plan.ExportPdf(path).Should().BeTrue();

            File.ReadAllBytes(path).Take(5).Should().Equal("%PDF-"u8.ToArray());
            using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            document.PageCount.Should().BeGreaterThanOrEqualTo(1);
            shell.Plan.StatusMessage.Should().StartWith("Plan exported");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Export_ToAnInvalidPath_ReportsTheFailure()
    {
        var shell = await CreateAsync(new FakePlanService { Entries = [new("M42")] });

        shell.Plan.ExportIcs(Path.Combine(Path.GetTempPath(), "missing-folder-" + Guid.NewGuid().ToString("N"), "plan.ics")).Should().BeFalse();

        shell.Plan.StatusMessage.Should().NotBeNullOrEmpty();
    }
}
