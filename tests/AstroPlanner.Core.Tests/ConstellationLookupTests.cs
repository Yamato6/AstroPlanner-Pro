using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Tests;

public sealed class ConstellationLookupTests
{
    /// <summary>Test positions published in the ReadMe of CDS VI/42 (Roman 1987), equinox B1950.0.</summary>
    [Theory]
    [InlineData(9.0, 65.0, "UMa")]
    [InlineData(23.5, -20.0, "Aqr")]
    [InlineData(5.12, 9.12, "Ori")]
    [InlineData(9.4555, -19.9, "Hya")]
    [InlineData(12.8888, 22.0, "Com")]
    [InlineData(15.6687, -12.1234, "Lib")]
    [InlineData(19.0, -40.0, "CrA")]
    [InlineData(6.2222, -81.1234, "Men")]
    public void Find_Roman1987TestPositions(double rightAscensionHours, double declinationDegrees, string expected)
    {
        var constellation = ConstellationLookup.Find(
            new EquatorialCoordinates(rightAscensionHours, declinationDegrees), Precession.B1950JulianDate);

        constellation.Abbreviation.Should().Be(expected);
    }

    [Theory]
    [InlineData(2.5302, 89.2641, "UMi", "Ursa Minor")]         // Polaris
    [InlineData(6.7525, -16.7161, "CMa", "Canis Major")]       // Sirius
    [InlineData(5.9195, 7.4071, "Ori", "Orion")]               // Betelgeuse
    [InlineData(0.7123, 41.2689, "And", "Andromeda")]          // M31
    [InlineData(14.2610, 19.1825, "Boo", "Boötes")]            // Arcturus
    [InlineData(18.6156, 38.7837, "Lyr", "Lyra")]              // Vega
    [InlineData(17.7614, -29.0078, "Sgr", "Sagittarius")]      // Galactic centre (Sgr A*)
    [InlineData(12.4433, -63.0991, "Cru", "Crux")]             // Acrux
    public void FromJ2000_KnownObjects(double rightAscensionHours, double declinationDegrees, string abbreviation, string name)
    {
        var constellation = ConstellationLookup.FromJ2000(new EquatorialCoordinates(rightAscensionHours, declinationDegrees));

        constellation.Should().Be(new Constellation(abbreviation, name));
    }

    [Fact]
    public void All_HasThe88IauConstellations()
    {
        var all = ConstellationLookup.All;

        all.Should().HaveCount(88);
        all.Select(c => c.Abbreviation).Should().OnlyHaveUniqueItems().And.OnlyContain(a => a.Length == 3);
        all.Select(c => c.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void FindByAbbreviation_IsCaseInsensitive()
    {
        ConstellationLookup.FindByAbbreviation("uma")!.Name.Should().Be("Ursa Major");
        ConstellationLookup.FindByAbbreviation("XYZ").Should().BeNull();
    }

    [Fact]
    public void Find_CoversTheWholeSky()
    {
        for (var rightAscension = 0.0; rightAscension < 24; rightAscension += 0.5)
        {
            for (var declination = -90.0; declination <= 90; declination += 5)
            {
                var act = () => ConstellationLookup.FromJ2000(new EquatorialCoordinates(rightAscension, declination));
                act.Should().NotThrow();
            }
        }
    }
}
