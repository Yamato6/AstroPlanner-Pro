using AstroPlanner.Core.Models;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>
/// View model tests without UI, with in-memory doubles for catalog, settings, plan and log.
/// </summary>
public sealed class ViewModelTests
{
    private static readonly CelestialObject M42 = TestCatalog.M42;
    private static readonly CelestialObject M31 = TestCatalog.M31;
    private static readonly CelestialObject M13 = TestCatalog.M13;

    private readonly FakeSettingsService _settingsService = new(new ObserverSettings { MinimumAltitude = 35 });
    private readonly FakePlanService _planService = new();

    private SettingsViewModel CreateSettings() =>
        new(_settingsService, new FakeCityRepository(), new NullLogger());

    private async Task<ShellViewModel> CreateLoadedMainAsync()
    {
        var viewModel = TestShell.Create([M42, M31, M13], _planService, _settingsService);
        // Winter night in Mexico City: M42 high, M13 below the horizon.
        viewModel.ObservationDateTime = new DateTime(2026, 1, 15, 22, 0, 0);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task Load_FillsTableSortedByAltitudeAndSelectsFirstRow()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Targets.CatalogCount.Should().Be(3);
        viewModel.Targets.Objects.Should().HaveCount(3);
        viewModel.Targets.Objects.Select(o => o.AltitudeDegrees).Should().BeInDescendingOrder();
        viewModel.Targets.SelectedObject.Should().BeSameAs(viewModel.Targets.Objects[0]);
        viewModel.Detail.Observation.Should().NotBeNull();
        viewModel.Targets.ValidationMessage.Should().BeNull();
    }

    [Fact]
    public async Task SelectedObservation_MatchesTablePosition()
    {
        var viewModel = await CreateLoadedMainAsync();

        var row = viewModel.Targets.SelectedObject!;
        var detail = viewModel.Detail.Observation!;

        detail.Coordinates.AltitudeDegrees.Should().BeApproximately(row.AltitudeDegrees, 1e-9);
        detail.Coordinates.AzimuthDegrees.Should().BeApproximately(row.AzimuthDegrees, 1e-9);
    }

    [Fact]
    public async Task OnlyVisible_HidesObjectsBelowThreshold()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Targets.OnlyVisible = true;

        viewModel.Targets.Objects.Should().NotBeEmpty().And.OnlyContain(o => o.IsVisible);
        viewModel.Targets.Objects.Should().NotContain(o => o.Id == "M13");
    }

    [Fact]
    public async Task ResetFilters_RestoresSavedMinimumAltitude()
    {
        var viewModel = await CreateLoadedMainAsync();
        viewModel.Observer.MinimumAltitude = 60;
        viewModel.Targets.TypeFilter = nameof(CelestialObjectType.Galaxy);

        viewModel.Targets.ResetFiltersCommand.Execute(null);

        viewModel.Observer.MinimumAltitude.Should().Be(35);
        viewModel.Targets.TypeFilter.Should().Be("All");
        viewModel.Targets.Objects.Should().HaveCount(3);
    }

    [Fact]
    public async Task Filters_ShowEnglishLabelsAndCanBeCleared()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Targets.TypeFilters[0].Should().Be(new FilterOption("All", "All"));
        viewModel.Targets.TypeFilters.Should().Contain(new FilterOption("GlobularCluster", "Globular cluster"));
        viewModel.Targets.ConstellationFilters[0].Should().Be(new FilterOption("", "All"));

        // Choose a constellation and go back to "All" from the list itself.
        var constellation = viewModel.Targets.ConstellationFilters[1].Value;
        viewModel.Targets.ConstellationFilter = constellation;
        viewModel.Targets.Objects.Should().OnlyContain(o => o.Constellation == constellation);
        viewModel.Targets.ConstellationFilter = viewModel.Targets.ConstellationFilters[0].Value;
        viewModel.Targets.Objects.Should().HaveCount(3);
    }

    [Fact]
    public async Task InvalidMinimumAltitude_ShowsWarningAndKeepsLastTable()
    {
        var viewModel = await CreateLoadedMainAsync();
        var before = viewModel.Targets.Objects.ToList();

        viewModel.Observer.MinimumAltitude = 120;

        viewModel.Targets.ValidationMessage.Should().Contain("Minimum altitude");
        viewModel.Observer.HasErrors.Should().BeTrue();
        viewModel.Targets.Objects.Should().Equal(before);

        viewModel.Observer.MinimumAltitude = 30;

        viewModel.Targets.ValidationMessage.Should().BeNull();
        viewModel.Observer.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task AfterMidnight_DetailUsesTheNightInProgress()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.ObservationDateTime = new DateTime(2026, 1, 15, 2, 0, 0);

        // The night in progress goes from sunset on the 14th to sunrise on the 15th.
        var night = viewModel.Observer.Night!;
        night.WindowStartLocal.Date.Should().Be(new DateTime(2026, 1, 14));
        night.WindowEndLocal.Date.Should().Be(new DateTime(2026, 1, 15));

        var culmination = viewModel.Detail.Observation!.BestObservationTimeLocal!.Value;
        culmination.Should().BeOnOrAfter(night.WindowStartLocal).And.BeOnOrBefore(night.WindowEndLocal);
        viewModel.Detail.GetNightAltitudeCurve()[0].LocalTime.Should().Be(night.WindowStartLocal);
    }

    [Fact]
    public async Task Night_InMexicoCityWinter_UsesSunsetAndAstronomicalDarkness()
    {
        var viewModel = await CreateLoadedMainAsync();

        // Mexico City, 2026-01-15: sunset ≈ 18:16, sunrise ≈ 07:13 (UTC-6).
        viewModel.Observer.Night!.SunsetLocal.Should().BeCloseTo(new DateTime(2026, 1, 15, 18, 16, 0), TimeSpan.FromMinutes(5));
        viewModel.Observer.Night.SunriseLocal.Should().BeCloseTo(new DateTime(2026, 1, 16, 7, 13, 0), TimeSpan.FromMinutes(5));
        viewModel.Tonight.DarkNightText.Should().MatchRegex(@"^\d\d:\d\d → \d\d:\d\d$");
        viewModel.Detail.ChartTitle.Should().Contain("→");
    }

    [Fact]
    public async Task SkyBanner_WarnsDuringTheDayAndClearsAtNight()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.ObservationDateTime = new DateTime(2026, 1, 15, 13, 0, 0);
        viewModel.Tonight.SkyCondition.Should().Be(SkyCondition.Day);
        viewModel.Tonight.SkyBanner.Should().StartWith("It is daytime");

        viewModel.ObservationDateTime = new DateTime(2026, 1, 15, 23, 0, 0);
        viewModel.Tonight.SkyCondition.Should().Be(SkyCondition.Dark);
        viewModel.Tonight.SkyBanner.Should().BeNull();
    }

    [Fact]
    public async Task Moon_IsReportedAndSeparationComputedForEachRow()
    {
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Tonight.Moon.Should().NotBeNull();
        viewModel.Tonight.MoonIlluminationText.Should().EndWith("%");
        viewModel.Targets.Objects.Should().OnlyContain(o => o.MoonSeparationDegrees >= 0 && o.MoonSeparationDegrees <= 180);
        viewModel.Detail.Observation!.MoonSeparationDegrees.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0.01, true, "New moon")]
    [InlineData(0.25, true, "Waxing crescent")]
    [InlineData(0.5, true, "First quarter")]
    [InlineData(0.8, false, "Waning gibbous")]
    [InlineData(0.99, false, "Full moon")]
    public void DescribeMoonPhase_NamesThePhase(double fraction, bool waxing, string expected)
    {
        DisplayNames.MoonPhase(fraction, waxing).Should().Be(expected);
    }

    [Fact]
    public async Task ObservationPlan_IsRestoredByIdAndSavedOnChanges()
    {
        _planService.Entries = [new("M31", "with filter"), new("M999")];
        var viewModel = await CreateLoadedMainAsync();

        viewModel.Plan.Items.Select(o => o.Id).Should().Equal("M31");

        viewModel.Targets.SelectedObject = viewModel.Targets.Objects.Single(o => o.Id == "M42");
        viewModel.Detail.AddToPlanCommand.Execute(null);

        _planService.Ids.Should().Equal("M31", "M42");
        viewModel.Detail.IsInPlan.Should().BeTrue();

        viewModel.Plan.RemoveCommand.Execute(viewModel.Plan.Items[0]);

        _planService.Ids.Should().Equal("M42");
    }

    [Fact]
    public void Settings_SaveWithInvalidLatitude_IsRejectedAndNotPersisted()
    {
        var settings = CreateSettings();
        settings.UseManualLocationCommand.Execute(null);
        settings.Latitude = 100;

        settings.SaveCommand.Execute(null);

        settings.HasError.Should().BeTrue();
        settings.StatusMessage.Should().Contain("Latitude");
        _settingsService.SaveCount.Should().Be(0);
    }

    [Fact]
    public void Settings_RevertChanges_DiscardsUnsavedEdits()
    {
        var settings = CreateSettings();
        settings.MinimumAltitude = 70;

        settings.RevertChanges();

        settings.MinimumAltitude.Should().Be(35);
    }
}
