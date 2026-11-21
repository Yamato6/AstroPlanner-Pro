using System.Globalization;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>M6b: night-vision theme, photographed logbook entries, predictions vs results and the advanced calibration.</summary>
public sealed class PolishUiTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Aguascalientes (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private static ObserverSettings Aguascalientes() => new()
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

    private readonly FakeLogService _log = new();

    private async Task<(ShellViewModel Shell, FakeSettingsService Settings)> CreateAsync(
        ObserverSettings? settings = null, FakePlanService? plan = null)
    {
        var settingsService = new FakeSettingsService(settings ?? Aguascalientes());
        var shell = TestShell.Create(
            catalog: [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13],
            settings: settingsService,
            plan: plan,
            logbook: new LogbookViewModel(_log, new NullLogger()),
            time: new ManualTimeProvider(WinterNightUtc),
            equipment: new FakeEquipmentRepository(EquipmentInventory.Demo),
            atlas: new FakeAtlas(1.84));
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        await shell.Targets.FeasibilityTask;
        return (shell, settingsService);
    }

    // ─── Night-vision theme ─────────────────────────────────────────────

    [Fact]
    public async Task NightTheme_IsSavedAtOnce_AndSentToTheSky()
    {
        var (shell, settings) = await CreateAsync();
        var channel = new FakeSkyChannel();
        shell.Sky.Attach(channel);
        channel.Ready();
        channel.Last("init").GetProperty("theme").GetString().Should().Be("dark");
        var changes = new List<string?>();
        shell.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        shell.IsNightTheme = true;

        settings.Load().Theme.Should().Be(AppTheme.Night);
        shell.Settings.IsNightTheme.Should().BeTrue("the settings window shows the same choice");
        changes.Should().Contain(nameof(ShellViewModel.IsNightTheme));
        channel.Last("setTheme").GetProperty("theme").GetString().Should().Be("night");

        shell.IsNightTheme = false;

        settings.Load().Theme.Should().Be(AppTheme.Dark);
        channel.Last("setTheme").GetProperty("theme").GetString().Should().Be("dark");
    }

    [Fact]
    public async Task SavedNightTheme_IsAppliedAtStartup_AndByTheSettingsWindow()
    {
        var saved = Aguascalientes();
        saved.Theme = AppTheme.Night;
        var (shell, settings) = await CreateAsync(saved);
        var channel = new FakeSkyChannel();
        shell.Sky.Attach(channel);
        channel.Ready();

        shell.Settings.Theme.IsNight.Should().BeTrue();
        channel.Last("init").GetProperty("theme").GetString().Should().Be("night");

        shell.Settings.IsNightTheme = false;
        shell.Settings.SaveCommand.Execute(null);

        settings.Load().Theme.Should().Be(AppTheme.Dark);
        shell.IsNightTheme.Should().BeFalse();
    }

    // ─── Advanced calibration ───────────────────────────────────────────

    [Fact]
    public async Task AdvancedConstants_ChangeTheVerdicts_AndResetToTheDefaults()
    {
        var (shell, settings) = await CreateAsync();
        var before = shell.Targets.AllObjects.Single(r => r.Id == "M42").Feasibility!;

        shell.Settings.TuningSnrMarginal = 20;
        shell.Settings.TuningSnrGood = 60;
        shell.Settings.TuningSnrExcellent = 99;
        shell.Settings.SaveCommand.Execute(null);
        await shell.Targets.FeasibilityTask;

        settings.Load().Tuning.SnrGood.Should().Be(60);
        settings.Load().Tuning.SeeingArcsec.Should().BeNull("untouched constants keep following the defaults");
        var after = shell.Targets.AllObjects.Single(r => r.Id == "M42").Feasibility!;
        after.Score.Should().BeLessThan(before.Score, "stricter SNR thresholds lower the score");

        shell.Settings.ResetTuningCommand.Execute(null);
        shell.Settings.SaveCommand.Execute(null);
        await shell.Targets.FeasibilityTask;

        settings.Load().Tuning.IsDefault.Should().BeTrue();
        shell.Targets.AllObjects.Single(r => r.Id == "M42").Feasibility!.Score.Should().Be(before.Score);
    }

    [Fact]
    public async Task AdvancedConstants_AreValidated()
    {
        var (shell, settings) = await CreateAsync();

        shell.Settings.TuningSnrGood = 2;   // below Marginal (3)
        shell.Settings.SaveCommand.Execute(null);

        shell.Settings.HasError.Should().BeTrue();
        shell.Settings.StatusMessage.Should().Be(Strings.AdvancedSnrOrder);
        settings.SaveCount.Should().Be(0);

        shell.Settings.TuningSnrGood = 10;
        shell.Settings.TuningSeeingArcsec = 20;
        shell.Settings.SaveCommand.Execute(null);

        shell.Settings.StatusMessage.Should().Be(Strings.AdvancedSeeingRange);
    }

    // ─── Photographed entries ───────────────────────────────────────────

    [Fact]
    public async Task LogDraft_OfAPlannedTarget_IsPhotographed_WithTheBlockSubsAndTheRig()
    {
        var (shell, _) = await CreateAsync(plan: new FakePlanService { Entries = [new("M42")] });
        var block = shell.Plan.Items.Single().Schedule!;
        shell.Targets.SelectById("M42");

        var draft = shell.Detail.CreateLogDraft()!;

        draft.IsPhotographed.Should().BeTrue();
        draft.RigName.Should().Be(EquipmentInventory.Demo.ActiveRig!.Name);
        draft.SubSecondsText.Should().Be(block.SubSeconds.ToString("0.#", CultureInfo.CurrentCulture));
        draft.SubCountText.Should().Be(block.SubCount.ToString(CultureInfo.CurrentCulture));
        draft.IsoText.Should().Be("800");
        draft.Prediction.Should().NotBeNull();
        draft.PredictionText.Should().Contain(FeasibilityText.Verdict(draft.Prediction!.Verdict));

        draft.SubCountText = "25";
        draft.ImagePath = @"C:\Astro\M42-stack.tif";
        draft.Rating = 4;
        shell.Logbook.Add(draft).Should().BeTrue();

        var entry = _log.Entries.Single();
        entry.Kind.Should().Be(LogEntryKind.Photographed);
        entry.Capture!.SubCount.Should().Be(25);
        entry.Capture.Iso.Should().Be(800);
        entry.Capture.RigId.Should().Be(EquipmentInventory.Demo.ActiveRigId);
        entry.Capture.ImagePath.Should().Be(@"C:\Astro\M42-stack.tif");
        entry.Prediction.Should().Be(draft.Prediction);
        DisplayNames.Capture(entry).Should().Contain("× 25").And.Contain("ISO 800");
        shell.Logbook.PhotographedMessierCount.Should().Be(1);
        shell.Logbook.MessierPhotographedText.Should().Be(DisplayNames.Format(Strings.LogbookPhotographedFormat, 1));
    }

    [Fact]
    public async Task LogDraft_WithoutAPlanBlock_IsObserved_AndKeepsNoCapture()
    {
        var (shell, _) = await CreateAsync();
        shell.Targets.SelectById("M31");

        var draft = shell.Detail.CreateLogDraft()!;
        draft.IsPhotographed.Should().BeFalse();
        draft.SubSecondsText.Should().NotBeEmpty("the model's recommendation is ready if the user ticks Photographed");
        shell.Logbook.Add(draft).Should().BeTrue();

        var entry = _log.Entries.Single();
        entry.Kind.Should().Be(LogEntryKind.Observed);
        entry.Capture.Should().BeNull();
        entry.Prediction.Should().BeNull("only photographed entries are compared with the model");
        shell.Logbook.PhotographedMessierCount.Should().Be(0);
        shell.Logbook.ObservedMessierCount.Should().Be(1);
    }

    [Fact]
    public void InvalidCaptureNumbers_StopTheEntry()
    {
        var logbook = new LogbookViewModel(_log, new NullLogger());
        var draft = logbook.CreateDraft("M42", "Orion Nebula", new DateTime(2026, 1, 15, 22, 0, 0), "Home",
            new CaptureDetails(), null, photographed: true);

        draft.SubSecondsText = "-30";
        draft.GainText = "abc";

        logbook.Add(draft).Should().BeFalse();
        draft.GetErrors(nameof(LogEntryDraftViewModel.SubSecondsText)).Should().ContainSingle();
        draft.GetErrors(nameof(LogEntryDraftViewModel.GainText)).Should().ContainSingle();
        _log.SaveCount.Should().Be(0);
    }

    // ─── Predictions vs results ─────────────────────────────────────────

    [Fact]
    public void Predictions_CompareTheScoreWithTheRating()
    {
        _log.Entries =
        [
            Photographed("M42", FeasibilityVerdict.Excellent, 90, rating: 5, day: 10),
            Photographed("M31", FeasibilityVerdict.Good, 60, rating: 3, day: 11),
            Photographed("M13", FeasibilityVerdict.Marginal, 35, rating: 2, day: 12),
            new ObservationLogEntry(Guid.NewGuid(), "M45", "Pleiades", new DateTime(2026, 1, 13, 21, 0, 0), "", "", 3, 3, 4, "")
        ];

        var logbook = new LogbookViewModel(_log, new NullLogger());

        logbook.HasPredictions.Should().BeTrue();
        logbook.Predictions.Points.Should().HaveCount(3, "the visual observation has no prediction");
        logbook.Predictions.Correlation.Should().BeGreaterThan(0.9);
        logbook.PredictionLines.Should().HaveCount(3);
        logbook.PredictionLines[0].Should().StartWith(Strings.VerdictExcellent);
        logbook.PredictionSummaryText.Should().Contain("3");
        logbook.BuildCsv(CultureInfo.InvariantCulture).Should().Contain(Strings.LogKindPhotographed).And.Contain(",90");
    }

    [Fact]
    public void Predictions_WithoutPhotographedEntries_ExplainHowToGetThem()
    {
        var logbook = new LogbookViewModel(_log, new NullLogger());

        logbook.HasPredictions.Should().BeFalse();
        logbook.PredictionSummaryText.Should().Be(Strings.PredictionsEmpty);
    }

    private static ObservationLogEntry Photographed(string id, FeasibilityVerdict verdict, int score, int rating, int day) =>
        new(Guid.NewGuid(), id, id, new DateTime(2026, 1, day, 22, 0, 0), "Home", "", 3, 3, rating, "")
        {
            Kind = LogEntryKind.Photographed,
            Capture = new CaptureDetails { RigName = "Rig", SubSeconds = 30, SubCount = 40 },
            Prediction = new PredictionSummary(verdict, score, score / 3.0)
        };
}
