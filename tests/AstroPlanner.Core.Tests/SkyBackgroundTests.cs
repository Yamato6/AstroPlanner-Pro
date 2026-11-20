using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.SkyQuality;

namespace AstroPlanner.Core.Tests;

public sealed class SkyBackgroundTests
{
    [Fact]
    public void KrisciunasSchaefer_FullMoonCase_MatchesTheHandEvaluatedEquations()
    {
        // Full Moon (α = 0) at the zenith, sky 90° away at the zenith distance 0 (X = 1),
        // k = 0.172 (Mauna Kea, as in the paper). By hand from the published equations:
        // I* = 10^(−0.4·3.84) = 0.029107; f(90°) = 10^5.36·1.06 + 10^3.9 = 250 775;
        // 10^(−0.4·0.172) = 0.85361 → B = 250 775 · 0.029107 · 0.85361 · 0.14639 = 912.1 nL
        // → V = (20.7233 − ln(912.1 / 34.08)) / 0.92104 = 18.93 mag/arcsec².
        KrisciunasSchaefer.MoonIlluminance(0).Should().BeApproximately(0.029107, 1e-6);
        KrisciunasSchaefer.ScatteringFunction(90).Should().BeApproximately(250_775, 5);
        KrisciunasSchaefer.Airmass(0).Should().Be(1);

        var nanolamberts = KrisciunasSchaefer.MoonlightNanolamberts(0, 90, 0, 0, KrisciunasSchaefer.MaunaKeaExtinction);

        nanolamberts.Should().BeApproximately(912.1, 1);
        KrisciunasSchaefer.MagnitudeFromNanolamberts(nanolamberts).Should().BeApproximately(18.93, 0.01);
    }

    [Fact]
    public void KrisciunasSchaefer_ConversionRoundTrips()
    {
        KrisciunasSchaefer.MagnitudeFromNanolamberts(KrisciunasSchaefer.NanolambertsFromMagnitude(21.5)).Should().BeApproximately(21.5, 1e-9);
        // A dark sky of 21.9 V mag/arcsec² is ≈ 54 nL (the paper's typical B0 at Mauna Kea is 79 nL).
        KrisciunasSchaefer.NanolambertsFromMagnitude(21.9).Should().BeInRange(50, 60);
    }

    [Fact]
    public void KrisciunasSchaefer_IsBrighterNearTheMoon_AndAtFullPhase()
    {
        double At(double alpha, double rho) => KrisciunasSchaefer.MoonlightNanolamberts(alpha, rho, 40, 40, 0.2);

        At(0, 10).Should().BeGreaterThan(At(0, 60));
        At(0, 60).Should().BeGreaterThan(At(0, 120));
        At(0, 60).Should().BeGreaterThan(At(90, 60));
        At(90, 60).Should().BeGreaterThan(At(150, 60));
        KrisciunasSchaefer.MoonlightNanolamberts(0, 60, 95, 40, 0.2).Should().Be(0);   // Moon below the horizon
        KrisciunasSchaefer.Airmass(90).Should().BeApproximately(5, 1e-9);
    }

    [Fact]
    public void ZenithBaseline_PrefersManualThenAtlasThenNatural()
    {
        SkyBackground.ZenithBaseline(20.5, 3.0).Should().Be((20.5, SkyQualitySource.Manual));
        var (atlas, source) = SkyBackground.ZenithBaseline(null, 1.84);
        source.Should().Be(SkyQualitySource.Atlas);
        // −2.5 log10((1.84 + 0.171) / 1.08e8) = 19.32 (Aguascalientes centre).
        atlas.Should().BeApproximately(19.32, 0.01);
        SkyBackground.ZenithBaseline(null, null).Should().Be((22.0, SkyQualitySource.Default));
        SkyBrightness.SqmFromArtificial(0).Should().BeApproximately(22.0, 0.01);
        SkyBrightness.SqmFromArtificial(1e6).Should().Be(SkyBrightness.MinimumSqm);
    }

    [Fact]
    public void SiteGlow_BrightensTowardTheHorizon()
    {
        SkyBackground.SiteGlowAt(21, 90).Should().BeApproximately(21, 1e-3);   // Kasten–Young X(90°) = 1.0003
        // X(30°) ≈ 1.995 → factor 1.4975 → 0.44 mag brighter.
        SkyBackground.SiteGlowAt(21, 30).Should().BeApproximately(21 - 0.438, 0.005);
        SkyBackground.SiteGlowAt(21, 20).Should().BeLessThan(SkyBackground.SiteGlowAt(21, 30));
        SkyBackground.SiteGlowAt(21, -5).Should().Be(SkyBackground.SiteGlowAt(21, 0));
        SkyBackground.SiteGlowAt(21, 30, 0).Should().BeApproximately(21, 1e-9);
    }

    [Fact]
    public void At_AddsMoonlightAndTwilight_InLinearUnits()
    {
        var dark = SkyBackground.At(21.5, 60, -30, false, -10, 0, 50, 0.25);
        dark.MoonlightSqm.Should().Be(double.PositiveInfinity);
        dark.TotalSqm.Should().BeApproximately(dark.SiteSqm, 1e-9);

        var moonlit = SkyBackground.At(21.5, 60, -30, false, 50, 0, 30, 0.25);
        moonlit.TotalSqm.Should().BeLessThan(dark.TotalSqm - 2);
        moonlit.TotalSqm.Should().BeApproximately(SkyBrightness.Combine(moonlit.SiteSqm, moonlit.MoonlightSqm), 1e-9);

        var twilight = SkyBackground.At(21.5, 60, -13, true, -10, 0, 50, 0.25);
        twilight.TwilightSqm.Should().BeLessThan(20);
        twilight.TotalSqm.Should().BeLessThan(dark.TotalSqm);
        SkyBackground.At(21.5, 60, -13, false, -10, 0, 50, 0.25).TwilightSqm.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void Refraction_ScalesWithThePressureOfTheSiteElevation()
    {
        Refraction.PressureScale(0).Should().Be(1);
        // Standard atmosphere: 1888 m → 0.7955 of the sea-level pressure.
        Refraction.PressureScale(1888).Should().BeApproximately(0.7955, 0.001);
        Refraction.PressureScale(double.NaN).Should().Be(1);
        Refraction.CorrectionDegrees(10, Refraction.PressureScale(1888))
            .Should().BeApproximately(0.7955 * Refraction.CorrectionDegrees(10), 1e-4);
        Refraction.ApparentAltitude(45).Should().Be(Refraction.ApparentAltitude(45, 1));
    }
}
