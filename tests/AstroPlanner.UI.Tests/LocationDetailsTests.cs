using AstroPlanner.Core.Models;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>Horizon profile and SQM of a saved site (spec §4.2 horizon editor [P1], §4.8).</summary>
public sealed class LocationDetailsTests
{
    private static readonly Location Home = new(
        "home", "Home", 19.4326, -99.1332, 2240, "America/Mexico_City", "Central Standard Time (Mexico)",
        new HorizonMask([new HorizonPoint(90, 15), new HorizonPoint(270, 5)]));

    private static readonly Location Dark = new(
        "dark", "Dark site", 21.1, -100.2, 2500, "America/Mexico_City", "Central Standard Time (Mexico)", zenithSqm: 21.6);

    private readonly FakeLocationRepository _repository = new();

    private ShellViewModel Create()
    {
        _repository.Locations.Add(Home);
        _repository.Locations.Add(Dark);
        var settings = new FakeSettingsService(new ObserverSettings { IsManualLocation = true, ActiveLocationId = "home" });
        return TestShell.Create(settings: settings, locations: _repository);
    }

    [Fact]
    public void TheActiveSite_SuppliesTheHorizonAndTheSqm()
    {
        var shell = Create();

        shell.Locations.ActiveLocation!.Id.Should().Be("home");
        shell.Observer.HorizonMask.Should().Be(Home.HorizonMask);
        shell.Observer.ZenithSqm.Should().BeNull();
        shell.Locations.Locations.Single(l => l.Id == "dark").SqmText.Should().Contain("B4");
    }

    [Fact]
    public void SelectingASite_LoadsItsDetails()
    {
        var locations = Create().Locations;

        locations.HasSelection.Should().BeFalse();
        locations.SelectedLocation = locations.Locations.Single(l => l.Id == "dark");

        locations.HorizonPoints.Should().BeEmpty();
        locations.ZenithSqmText.Should().Be(21.6.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture));
        locations.BortleText.Should().Contain("4");

        locations.SelectedLocation = locations.Locations.Single(l => l.Id == "home");
        locations.HorizonPoints.Select(p => (p.Azimuth, p.Altitude)).Should().Equal((90.0, 15.0), (270.0, 5.0));
        locations.ZenithSqmText.Should().BeEmpty();
        locations.GetHorizonPreview().Should().HaveCount(361);
        locations.GetHorizonPreview()[180].AltitudeDegrees.Should().BeApproximately(10, 1e-9);
    }

    [Fact]
    public void SavingTheActiveSite_UpdatesTheStoreAndThePlannerAtOnce()
    {
        var shell = Create();
        var locations = shell.Locations;
        locations.SelectedLocation = locations.Locations.Single(l => l.Id == "home");
        var version = locations.HorizonVersion;

        locations.AddHorizonPointCommand.Execute(null);
        locations.HorizonPoints.Should().HaveCount(3);
        locations.HorizonPoints[^1].Azimuth.Should().Be(300);
        locations.HorizonPoints[^1].Altitude = 25;
        locations.HorizonVersion.Should().BeGreaterThan(version);
        locations.ZenithSqmText = "20.1";
        locations.SaveDetailsCommand.Execute(null);

        locations.DetailsHasError.Should().BeFalse();
        locations.DetailsMessage.Should().Be(Strings.LocationsDetailsSaved);
        var saved = _repository.Locations.Single(l => l.Id == "home");
        saved.HorizonMask.Points.Should().HaveCount(3);
        saved.ZenithSqm.Should().Be(20.1);
        shell.Observer.HorizonMask.Should().Be(saved.HorizonMask);
        shell.Observer.ZenithSqm.Should().Be(20.1);
        locations.SelectedLocation!.Id.Should().Be("home", "the edited site stays selected");

        locations.ClearHorizonCommand.Execute(null);
        locations.RemoveHorizonPointCommand.Execute(null);
        locations.ZenithSqmText = "";
        locations.SaveDetailsCommand.Execute(null);
        _repository.Locations.Single(l => l.Id == "home").HorizonMask.IsFlat.Should().BeTrue();
        shell.Observer.ZenithSqm.Should().BeNull();
    }

    [Fact]
    public void SavingAnotherSite_DoesNotTouchThePlanner()
    {
        var shell = Create();
        var locations = shell.Locations;
        locations.SelectedLocation = locations.Locations.Single(l => l.Id == "dark");

        locations.AddHorizonPointCommand.Execute(null);
        locations.SaveDetailsCommand.Execute(null);

        _repository.Locations.Single(l => l.Id == "dark").HorizonMask.Points.Should().ContainSingle();
        shell.Observer.HorizonMask.Should().Be(Home.HorizonMask);
        shell.Observer.ZenithSqm.Should().BeNull();
    }

    [Theory]
    [InlineData(400, 10, null)]
    [InlineData(90, 95, null)]
    [InlineData(270, 10, null)]
    [InlineData(10, 10, "25")]
    public void InvalidInput_IsReported_AndNothingIsSaved(double azimuth, double altitude, string? sqm)
    {
        var locations = Create().Locations;
        locations.SelectedLocation = locations.Locations.Single(l => l.Id == "home");
        locations.AddHorizonPointCommand.Execute(null);
        locations.HorizonPoints[^1].Azimuth = azimuth;
        locations.HorizonPoints[^1].Altitude = altitude;
        locations.ZenithSqmText = sqm ?? "";

        locations.SaveDetailsCommand.Execute(null);

        locations.DetailsHasError.Should().BeTrue();
        locations.DetailsMessage.Should().NotBeNullOrEmpty();
        _repository.Locations.Single(l => l.Id == "home").Should().Be(Home);
        locations.GetHorizonPreview().Should().HaveCount(sqm is null ? 0 : 361);
    }

    [Fact]
    public void RevertDetails_DiscardsTheEdits()
    {
        var locations = Create().Locations;
        locations.SelectedLocation = locations.Locations.Single(l => l.Id == "home");
        locations.ClearHorizonCommand.Execute(null);
        locations.ZenithSqmText = "18";

        locations.RevertDetailsCommand.Execute(null);

        locations.HorizonPoints.Should().HaveCount(2);
        locations.ZenithSqmText.Should().BeEmpty();
    }
}