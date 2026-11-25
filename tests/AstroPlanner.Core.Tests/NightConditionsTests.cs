using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.Core.Weather;

namespace AstroPlanner.Core.Tests;

/// <summary>M4: 7Timer scales, the night's dew/wind/seeing summary and its effect on feasibility.</summary>
public sealed class NightConditionsTests
{
    private static readonly DateTime Dusk = new(2026, 12, 10, 1, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Dawn = new(2026, 12, 10, 12, 0, 0, DateTimeKind.Utc);

    private static AstroForecast Astro(int seeing, int transparency) => new(21.88, -102.29,
        [.. Enumerable.Range(0, 24).Select(i => new AstroForecastPoint(Dusk.AddHours(-12 + 3 * i), 2, seeing, transparency, 2))],
        Dusk);

    private static WeatherForecast Weather(Func<int, double> temperature, Func<int, double> dewPoint, Func<int, double> gust) => new(21.88, -102.29,
        [.. Enumerable.Range(0, 48).Select(i => new HourlyWeather(Dusk.AddHours(-12 + i), 10)
        {
            TemperatureC = temperature(i), DewPointC = dewPoint(i), WindSpeedKmh = gust(i) / 2, WindGustKmh = gust(i)
        })],
        Dusk);

    [Theory]
    [InlineData(1, 0.4, 0.25)]
    [InlineData(3, 0.875, 0.45)]
    [InlineData(6, 1.75, 0.775)]
    [InlineData(8, 2.75, 1.1)]
    [InlineData(0, 0.4, 0.25)]   // out of range: clamped
    [InlineData(9, 2.75, 1.1)]
    public void SevenTimerClasses_AreTheMiddleOfTheDocumentedRanges(int value, double seeing, double transparency)
    {
        AstroForecastScales.SeeingArcsec(value).Should().Be(seeing);
        AstroForecastScales.TransparencyMagnitudesPerAirmass(value).Should().Be(transparency);
    }

    [Fact]
    public void AstroForecast_PointsCoverThreeHours()
    {
        var forecast = Astro(3, 2);

        forecast.At(Dusk.AddMinutes(80))!.TimeUtc.Should().Be(Dusk);
        forecast.At(Dusk.AddMinutes(100))!.TimeUtc.Should().Be(Dusk.AddHours(3));
        forecast.At(Dusk.AddDays(10)).Should().BeNull();
        // Dusk … dusk + 12 h: the last one starts 1.5 h before its time, inside the window.
        forecast.Between(Dusk, Dawn).Should().HaveCount(5);
    }

    [Fact]
    public void Conditions_AverageTheForecastsOverTheWindow()
    {
        var conditions = NightConditions.Create(Dusk, Dawn, Weather(_ => 12, _ => 2, _ => 10), Astro(3, 2));

        conditions.SeeingArcsec.Should().Be(0.875);
        conditions.TransparencyMagnitudesPerAirmass.Should().Be(0.35);
        conditions.MinimumDewMarginC.Should().Be(10);
        conditions.DewRisk.Should().Be(DewRisk.None);
        conditions.DewRiskFromUtc.Should().BeNull();
        conditions.MaximumGustKmh.Should().Be(10);
        conditions.WindWarning.Should().BeFalse();
    }

    [Fact]
    public void ClosingDewMargin_RaisesTheDewRisk_FromTheFirstHourAtRisk()
    {
        // The temperature drops 1 °C per hour toward a constant 5 °C dew point.
        var weather = Weather(i => 20 - i, _ => 5, _ => 5);

        var conditions = NightConditions.Create(Dusk, Dawn, weather, null);

        conditions.DewRisk.Should().Be(DewRisk.High);
        conditions.MinimumDewMarginC.Should().BeLessThanOrEqualTo(2);
        conditions.DewRiskFromUtc.Should().Be(Dusk);   // margin 3 °C at dusk, already "moderate"
        conditions.SeeingArcsec.Should().BeNull();
        NightConditions.Create(Dusk, Dawn, Weather(_ => 8, _ => 5, _ => 5), null).DewRisk.Should().Be(DewRisk.Moderate);
    }

    [Theory]
    [InlineData(MountKind.FixedTripod, 25, false)]
    [InlineData(MountKind.FixedTripod, 35, true)]
    [InlineData(MountKind.Equatorial, 25, true)]
    [InlineData(MountKind.AltAz, 15, false)]
    public void WindWarning_DependsOnTheMount(MountKind mount, double gust, bool warning)
    {
        var conditions = NightConditions.Create(Dusk, Dawn, Weather(_ => 15, _ => 0, _ => gust), null, mount);

        conditions.WindWarning.Should().Be(warning);
        conditions.GustLimitKmh.Should().Be(mount == MountKind.FixedTripod ? 30 : 20);
    }

    [Fact]
    public void NoForecast_GivesNoValues()
    {
        var conditions = NightConditions.Create(Dusk, Dawn, null, null);

        conditions.Should().BeEquivalentTo(NightConditions.None with { GustLimitKmh = 30 });
    }

    // --- Feasibility ------------------------------------------------------------------------------

    private static readonly TimeZoneInfo MexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
    private static readonly ObserverLocation Aguascalientes = new(21.88, -102.29, 1888);

    private static FeasibilityRequest Request(NightConditions? conditions)
    {
        var astronomy = new AstronomyService();
        var demo = EquipmentInventory.Demo.Resolve("t7-starsense-130")!;
        var rig = demo with { Mount = new Mount("eq", "EQ mount", MountKind.Equatorial) };
        var night = astronomy.GetNightInfo(Aguascalientes, new DateTime(2026, 12, 9, 21, 0, 0), MexicoZone);
        return new FeasibilityRequest(Aguascalientes, MexicoZone, night, 20, rig, 21.0, SkyQualitySource.Manual) { Conditions = conditions };
    }

    [Fact]
    public void ForecastSeeingAndHaze_ReplaceTheDefaults()
    {
        var request = Request(new NightConditions { SeeingArcsec = 1.125, TransparencyMagnitudesPerAirmass = 0.65 });

        request.EffectiveConstants.SeeingArcsec.Should().Be(1.125);
        request.HazeExtinction.Should().BeApproximately(0.35, 1e-12);
        request.EffectiveConstants.ExtinctionCoefficient.Should().BeApproximately(0.25 + 0.35, 1e-12);
        Request(null).EffectiveConstants.Should().Be(FeasibilityConstants.Default);
        Request(new NightConditions { TransparencyMagnitudesPerAirmass = 0.25 }).HazeExtinction.Should().Be(0);
    }

    [Fact]
    public void HazyNight_LowersTheSignal_AndSaysWhy()
    {
        var calculator = new FeasibilityCalculator(new AstronomyService());

        var clear = calculator.Evaluate(TestCatalog.M42, Request(new NightConditions { SeeingArcsec = 1.375, TransparencyMagnitudesPerAirmass = 0.25 }));
        var hazy = calculator.Evaluate(TestCatalog.M42, Request(new NightConditions { SeeingArcsec = 2.75, TransparencyMagnitudesPerAirmass = 1.1 }));

        hazy.SignalToNoise.Should().BeLessThan(clear.SignalToNoise);
        hazy.Sampling.FwhmPixels.Should().BeGreaterThan(clear.Sampling.FwhmPixels);
        clear.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Transparency && r.Impact == ReasonImpact.Positive);
        hazy.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Transparency && r.Impact == ReasonImpact.Negative && r.Value2 > 0.7);
        hazy.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Seeing && r.Impact == ReasonImpact.Negative && r.Value == 2.75);
    }

    [Fact]
    public void WithoutForecast_NothingChanges()
    {
        var calculator = new FeasibilityCalculator(new AstronomyService());

        var plain = calculator.Evaluate(TestCatalog.M42, Request(null));
        var empty = calculator.Evaluate(TestCatalog.M42, Request(NightConditions.None));

        empty.SignalToNoise.Should().Be(plain.SignalToNoise);
        plain.Reasons.Should().NotContain(r => r.Factor == FeasibilityFactor.Seeing || r.Factor == FeasibilityFactor.Transparency);
    }
}
