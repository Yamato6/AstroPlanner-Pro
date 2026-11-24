using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>WPF imaging decodes real survey tiles for the framing preview.</summary>
public sealed class WpfImageDecoderTests
{
    private static readonly HipsSurvey Dss = new("DSSColor", "DSS2 colour", 9);

    [Fact]
    public void RealDssTile_PutsThePleiadesWhereTheSkyHasThem()
    {
        // The recorded order-5 tile with the Pleiades, decoded by WPF and sampled by the preview:
        // the brightest stars must land on bright pixels (checks the tile layout end to end).
        var jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "online", "hips-dsscolor-norder5-npix115.jpg"));
        var tile = new WpfImageDecoder().Decode(jpeg);
        tile.Width.Should().Be(512);

        var plan = FramingPreview.Plan(new FramingRequest(56.40, 24.22, 1.0, 0.8, OutputWidth: 400), Dss with { MaximumOrder = 5 });
        plan.Order.Should().Be(5);
        var image = plan.Compose(new Dictionary<HipsTileKey, RgbImage> { [new HipsTileKey(5, 115)] = tile }, out _);

        var stars = new[] { (56.2189, 24.1133), (56.4566, 24.3678), (56.5816, 23.9484), (56.3021, 24.4672) };   // Electra, Maia, Merope, Taygeta
        foreach (var (ra, dec) in stars)
        {
            var (x, y) = plan.ToPixel(ra, dec)!.Value;
            var (r, g, b) = image[(int)Math.Round(x), (int)Math.Round(y)];
            ((r + g + b) / 3).Should().BeGreaterThan(150, $"star at RA {ra}, Dec {dec}");
        }

        var sky = image[20, 20];
        ((sky.R + sky.G + sky.B) / 3).Should().BeLessThan(90, "the sky away from the stars is dark");
        FluentActions.Invoking(() => new WpfImageDecoder().Decode([1, 2, 3])).Should().Throw<NotSupportedException>();
    }
}
