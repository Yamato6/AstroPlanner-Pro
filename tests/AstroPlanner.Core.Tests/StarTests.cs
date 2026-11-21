using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Tests;

/// <summary>M6a: star names and the double-star model.</summary>
public sealed class StarTests
{
    [Theory]
    [InlineData("Sirius", "Alp", 9, "CMa", "Sirius")]
    [InlineData(null, "Bet-1", 6, "Cyg", "β¹ Cyg")]
    [InlineData(null, "Ome", null, "Sco", "ω Sco")]
    [InlineData(null, null, 61, "Cyg", "61 Cyg")]
    [InlineData(null, null, null, "Cyg", null)]
    public void Labels_PreferTheNameThenBayerThenFlamsteed(string? proper, string? bayer, int? flamsteed, string constellation, string? expected) =>
        StarNames.Label(proper, bayer, flamsteed, constellation).Should().Be(expected);

    [Fact]
    public void SpelledNames_UseTheLatinGenitive()
    {
        StarNames.SpelledBayer("Alp", "CMa").Should().Be("Alpha Canis Majoris");
        StarNames.SpelledBayer("Bet-1", "Cyg").Should().Be("Beta1 Cygni");
        StarNames.SpelledBayer("Bet-1", "Cyg", withIndex: false).Should().Be("Beta Cygni");
        StarNames.GreekBayer("Bet-1", "Cyg", withIndex: false).Should().Be("β Cyg");
        StarNames.SpelledFlamsteed(61, "Cyg").Should().Be("61 Cygni");
        StarNames.Genitive("Boo").Should().Be("Boötis");
        StarNames.Genitive("Xyz").Should().BeNull();
        StarNames.SpelledBayer("Alp", "Xyz").Should().BeNull();
        StarNames.SpelledBayer(null, "Cyg").Should().BeNull();
        StarNames.SpelledFlamsteed(null, "Cyg").Should().BeNull();
    }

    [Fact]
    public void MainPair_IsTheBrightestCompanion_NotASpecklePair()
    {
        var epsilonLyrae = new StarInfo(91633, 91919, 173582, "F1V", 0.16, 49.8, null, null, null)
        {
            Pairs =
            [
                new DoubleStarPair("18443+3940", "STF2382", "AB", 2025, 342, 2.1, 5.15, 6.10),
                new DoubleStarPair("18443+3940", "STFA 37", "AI", 2023, 137, 150.0, 5.15, 10.12),
                new DoubleStarPair("18443+3940", "STFA 37", "AB,CD", 2021, 172, 209.4, 4.67, 4.56)
            ]
        };
        var speckleOnly = new StarInfo(1, null, null, null, null, null, null, null, null)
        {
            Pairs = [new DoubleStarPair("00000+0000", "BNU  10", "Aa,Ab", 2021, 55, 0.3, 3.4, 5.0)]
        };

        epsilonLyrae.MainPair!.Name.Should().Be("STFA 37 AB,CD", "the wide pair of the Double Double, with the brighter companion");
        epsilonLyrae.IsDouble.Should().BeTrue();
        epsilonLyrae.DistanceLightYears.Should().BeApproximately(162.4, 0.1);
        speckleOnly.Pairs[0].IsClose.Should().BeTrue();
        speckleOnly.MainPair!.DiscovererName.Should().Be("BNU 10", "a speckle pair is used when it is the only one");
        new StarInfo(2, null, null, null, null, null, null, null, null).MainPair.Should().BeNull();
    }
}
