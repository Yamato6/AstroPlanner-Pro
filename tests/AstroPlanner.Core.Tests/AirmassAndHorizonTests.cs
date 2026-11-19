using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Tests;

public sealed class AirmassTests
{
    [Fact]
    public void KastenYoung_AtTheZenith_IsOne()
    {
        Airmass.KastenYoung(90).Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void KastenYoung_AtTheHorizon_IsAbout38()
    {
        // Kasten & Young (1989): the formula gives 37.92 at 0° (their tabulated value is 38.09).
        Airmass.KastenYoung(0).Should().BeApproximately(37.92, 0.01);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(45)]
    [InlineData(30)]
    public void KastenYoung_HighInTheSky_IsCloseToThePlaneParallelSecant(double altitudeDegrees)
    {
        var secant = 1.0 / Math.Sin(altitudeDegrees * Math.PI / 180.0);

        Airmass.KastenYoung(altitudeDegrees).Should().BeApproximately(secant, secant * 0.005);
    }

    [Fact]
    public void KastenYoung_GrowsTowardsTheHorizon()
    {
        var values = Enumerable.Range(0, 91).Select(a => Airmass.KastenYoung(90 - a)).ToList();

        values.Should().BeInAscendingOrder();
    }

    [Fact]
    public void KastenYoung_FarBelowTheHorizon_IsInfinite()
    {
        Airmass.KastenYoung(-10).Should().Be(double.PositiveInfinity);
        Airmass.KastenYoung(double.NaN).Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void Extinction_IsCoefficientTimesAirmass()
    {
        Airmass.ExtinctionMagnitudes(2.0).Should().BeApproximately(0.5, 1e-12);
        Airmass.ExtinctionMagnitudes(1.5, 0.15).Should().BeApproximately(0.225, 1e-12);
    }

    [Theory]
    [InlineData(0.5, 0.25)]
    [InlineData(1.0, -0.1)]
    public void Extinction_InvalidArguments_Throw(double airmass, double coefficient)
    {
        var act = () => Airmass.ExtinctionMagnitudes(airmass, coefficient);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public sealed class HorizonMaskTests
{
    private static readonly HorizonMask Trees = new(
    [
        new HorizonPoint(90, 10),
        new HorizonPoint(0, 20),
        new HorizonPoint(270, 30)
    ]);

    [Fact]
    public void Flat_IsZeroEverywhere()
    {
        HorizonMask.Flat.IsFlat.Should().BeTrue();
        HorizonMask.Flat.AltitudeAt(123).Should().Be(0);
    }

    [Fact]
    public void AltitudeAt_InterpolatesLinearlyAndSortsThePoints()
    {
        Trees.Points.Select(p => p.AzimuthDegrees).Should().Equal(0, 90, 270);
        Trees.AltitudeAt(0).Should().Be(20);
        Trees.AltitudeAt(45).Should().BeApproximately(15, 1e-9);
        Trees.AltitudeAt(180).Should().BeApproximately(20, 1e-9);
    }

    [Fact]
    public void AltitudeAt_WrapsAroundNorth()
    {
        // From 270° (30°) to 360° = 0° (20°).
        Trees.AltitudeAt(315).Should().BeApproximately(25, 1e-9);
        Trees.AltitudeAt(-45).Should().BeApproximately(25, 1e-9);
        Trees.AltitudeAt(360).Should().BeApproximately(20, 1e-9);
    }

    [Fact]
    public void SinglePoint_IsAConstantHorizon()
    {
        new HorizonMask([new HorizonPoint(10, 5)]).AltitudeAt(200).Should().Be(5);
    }

    [Fact]
    public void Masks_WithTheSamePoints_AreEqual()
    {
        var copy = new HorizonMask([new HorizonPoint(270, 30), new HorizonPoint(0, 20), new HorizonPoint(90, 10)]);

        copy.Should().Be(Trees);
        copy.GetHashCode().Should().Be(Trees.GetHashCode());
    }

    [Fact]
    public void DuplicateAzimuth_Throws()
    {
        var act = () => new HorizonMask([new HorizonPoint(0, 1), new HorizonPoint(360, 2)]);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(361, 0)]
    [InlineData(0, 91)]
    public void InvalidPoint_Throws(double azimuth, double altitude)
    {
        var act = () => new HorizonMask([new HorizonPoint(azimuth, altitude)]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public sealed class LocationTests
{
    [Fact]
    public void Location_KeepsBothTimeZoneIdsAndConvertsToAnObserver()
    {
        var location = new Location("home", " Home ", 21.88, -102.29, 1888,
            "America/Mexico_City", "Central Standard Time (Mexico)");

        location.Name.Should().Be("Home");
        location.HorizonMask.Should().Be(HorizonMask.Flat);
        location.ToObserver().Should().Be(new ObserverLocation(21.88, -102.29, 1888));
    }

    [Fact]
    public void Location_InvalidCoordinates_Throw()
    {
        var act = () => new Location("x", "X", 95, 0, 0, "Etc/UTC", "UTC");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
