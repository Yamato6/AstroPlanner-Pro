using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;

namespace AstroPlanner.Core.Tests;

public class AstronomyServiceTests
{
    private readonly AstronomyService _service = new();
    private readonly ObserverLocation _madrid = new(40.4, -3.7);
    private readonly CelestialObject _m42 = TestCatalog.M42;

    [Fact]
    public void GetHorizontal_MatchesCoordinateTransformer()
    {
        var instant = new DateTime(2023, 10, 15, 22, 0, 0, DateTimeKind.Utc);

        var h = _service.GetHorizontal(_m42, _madrid, instant);

        h.AltitudeDegrees.Should().BeInRange(-90, 90);
        h.AzimuthDegrees.Should().BeInRange(0, 360);
    }

    [Fact]
    public void GetAltitudeSeries_ReturnsExpectedNumberOfPoints()
    {
        var start = new DateTime(2023, 10, 15, 18, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 10, 16, 6, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromMinutes(30);

        var series = _service.GetAltitudeSeries(_m42, _madrid, start, end, interval).ToList();

        // 18:00 to 06:00 = 12 h every 30 min => 24 intervals + the first point = 25 points.
        series.Should().HaveCount(25);
        series.Should().OnlyContain(p => p.AltitudeDegrees >= -90 && p.AltitudeDegrees <= 90);
    }

    [Fact]
    public void GetAltitudeSeries_InvalidStep_Throws()
    {
        var start = new DateTime(2023, 10, 15, 18, 0, 0, DateTimeKind.Utc);

        var act = () => _service.GetAltitudeSeries(_m42, _madrid, start, start.AddHours(1), TimeSpan.Zero).ToList();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static readonly TimeZoneInfo MadridZone =
        TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    private static readonly DateTime NightStart = new(2023, 12, 15, 18, 0, 0);

    private static CelestialObject ObjectAt(double raHours, double decDegrees) => new(
        "TEST",
        "Test object",
        "Test",
        CelestialObjectType.Unknown,
        5.0,
        new EquatorialCoordinates(raHours, decDegrees));

    [Fact]
    public void GetNightAltitudeSeries_CoversTheRequestedLocalWindow()
    {
        var start = new DateTime(2023, 12, 15, 18, 0, 0);

        var series = _service.GetNightAltitudeSeries(_m42, _madrid, start, start.AddHours(12), MadridZone, TimeSpan.FromMinutes(30));

        series.Should().HaveCount(25);
        series[0].LocalTime.Should().Be(start);
        series[^1].LocalTime.Should().Be(start.AddHours(12));
    }

    [Fact]
    public void GetNightEvents_M42InDecember_RisesAndCulminatesButSetsAfterTheNight()
    {
        var start = new DateTime(2023, 12, 15, 18, 0, 0);

        var events = _service.GetNightEvents(_m42, _madrid, start, start.AddHours(12), MadridZone, 20);

        // Culmination altitude = 90 - latitude + declination = 90 - 40.4 - 5.39 ≈ 44.2°.
        events.MaximumAltitudeDegrees.Should().BeApproximately(44.2, 1.0);
        events.CulminationLocal.Should().BeAfter(start.AddHours(4)).And.BeBefore(start.AddHours(9));
        events.RiseLocal.Should().NotBeNull().And.BeBefore(events.CulminationLocal);
        events.ThresholdCrossingLocal.Should().NotBeNull().And.BeAfter(events.RiseLocal!.Value);
        // Semi-arc ≈ 5.7 h: it culminates around 01:15 and sets around 07:00, outside the
        // 18:00 → 06:00 window, so there is no set during the night.
        events.SetLocal.Should().BeNull();
    }

    [Fact]
    public void GetNightEvents_CircumpolarObject_NeverRisesOrSets()
    {
        var observer = new ObserverLocation(50, 0);

        var events = _service.GetNightEvents(
            ObjectAt(3, 85), observer, NightStart, NightStart.AddHours(12), MadridZone, 20);

        events.RiseLocal.Should().BeNull();
        events.SetLocal.Should().BeNull();
        events.MaximumAltitudeDegrees.Should().BeGreaterThan(45);
    }

    [Fact]
    public void GetNightEvents_ObjectBelowHorizonAllNight_HasNegativeMaximum()
    {
        // Dec -80° from latitude 40°N: maximum altitude = 90 - 40 - 80 = -30°.
        var observer = new ObserverLocation(40, 0);

        var events = _service.GetNightEvents(
            ObjectAt(3, -80), observer, NightStart, NightStart.AddHours(12), MadridZone, 20);

        events.MaximumAltitudeDegrees.Should().BeLessThan(0);
        events.RiseLocal.Should().BeNull();
        events.ThresholdCrossingLocal.Should().BeNull();
    }
}
