using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Sky;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.UI.Tests;

public sealed class SkyAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MemoryStream Zip(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Extract_CreatesAHashFolder_ReusesIt_AndRemovesOldCopies()
    {
        var old = Path.Combine(_root, "0123456789abcdef");
        Directory.CreateDirectory(old);

        var first = SkyAssets.Extract(Zip(("app/index.html", "<html>"), ("data/stars.bin", "x")), _root);
        var second = SkyAssets.Extract(Zip(("app/index.html", "<html>"), ("data/stars.bin", "x")), _root);

        first.Should().NotBeNull();
        second.Should().Be(first);
        File.ReadAllText(Path.Combine(first!.AppDirectory, "index.html")).Should().Be("<html>");
        File.Exists(Path.Combine(first.DataDirectory, "stars.bin")).Should().BeTrue();
        Directory.Exists(old).Should().BeFalse();
        Directory.GetDirectories(_root).Should().ContainSingle();
    }

    [Fact]
    public void Extract_WithoutARenderer_ReturnsNull()
    {
        SkyAssets.Extract(Zip(("data/stars.bin", "x")), _root).Should().BeNull();
        Directory.GetDirectories(_root).Should().BeEmpty("the staging folder is cleaned up");
    }

    [Fact]
    public void TheBuild_EmbedsTheRenderer()
    {
        // Only when the UI was built with Node.js (always in CI); otherwise the app falls back.
        using var zip = SkyAssets.OpenEmbedded();
        if (zip is null)
        {
            return;
        }

        using var archive = new ZipArchive(zip);
        archive.Entries.Select(e => e.FullName).Should().Contain(["app/index.html", "data/stars.bin", "data/dsos.json"]);
    }
}

public sealed class SkyHostSecurityTests
{
    [Theory]
    [InlineData("https://app.astroplanner/index.html", true)]
    [InlineData("https://app.astroplanner/index.html?bench=1", true)]
    [InlineData("https://APP.astroplanner/assets/x.js", true)]
    [InlineData("https://data.astroplanner/stars.bin", false)]
    [InlineData("http://app.astroplanner/index.html", false)]
    [InlineData("https://app.astroplanner:8443/index.html", false)]
    [InlineData("https://app.astroplanner.evil.com/", false)]
    [InlineData("https://github.com/", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("about:blank", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("https://app.astroplanner/index.html?res=1920x1080&mode=finder", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheAppHostCanBeLoaded_OrSendMessages(string? uri, bool allowed)
    {
        SkyHost.IsAllowedNavigation(uri).Should().Be(allowed);
        SkyHost.IsTrustedSource(uri).Should().Be(allowed);
    }
}

public sealed class SkyLaunchOptionsTests
{
    [Fact]
    public void BenchmarkSwitches_BecomeRendererParameters()
    {
        SkyLaunchOptions.Query(["AstroPlannerPro.exe"]).Should().BeEmpty();
        SkyLaunchOptions.Query(["AstroPlannerPro.exe", "--sky-quality=HIGH", "--sky-landscape=open-field"])
            .Should().Be("?quality=high&landscape=open-field");
        SkyLaunchOptions.Query(["AstroPlannerPro.exe", "--sky-quality=ultra", "--sky-landscape=../x"]).Should().BeEmpty();
        SkyLaunchOptions.Query(["AstroPlannerPro.exe", "--sky-benchmark", "--sky-stars=20", "--sky-resolution=1920x1080", "--sky-mode=allsky"])
            .Should().Be("?bench=1&stars=8&res=1920x1080&mode=allSky");
    }

    [Theory]
    [InlineData("--sky-resolution=1920x1080&x=1")]
    [InlineData("--sky-resolution=big")]
    [InlineData("--sky-mode=<script>")]
    [InlineData("--sky-stars=many")]
    public void AnythingMalformed_NeverReachesTheUrl(string argument) =>
        SkyLaunchOptions.Query([argument]).Should().BeEmpty();

    [Theory]
    [InlineData(new[] { "AstroPlannerPro.exe" }, false)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--ui=web" }, true)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--UI=Web", "--sky-benchmark" }, true)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--ui=wpf" }, false)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--ui=web2" }, false)]
    public void UiWeb_OpensThePlanetariumUi_AnythingElseTheClassicOne(string[] args, bool web) =>
        SkyLaunchOptions.UseWebUi(args).Should().Be(web);

    [Fact]
    public void Pages_AreTheTwoBuiltFromTheWebWorkspace_OnTheAppHost()
    {
        SkyHost.PageUri(SkyPage.Classic, "").Should().Be(new Uri("https://app.astroplanner/index.html"));
        SkyHost.PageUri(SkyPage.Planetarium, "?bench=1").Should().Be(new Uri("https://app.astroplanner/planetarium.html?bench=1"));
        SkyHost.IsAllowedNavigation(SkyHost.PageUri(SkyPage.Planetarium, "").ToString()).Should().BeTrue();
    }
}

/// <summary>
/// WPF reports a broken binding only at run time, silently: every "Sky.…" path of the main
/// window must resolve, property by property, from ShellViewModel (the sky view model is split
/// into parts, Sky.Time, Sky.Layers, Sky.Camera, Sky.Search and Sky.Map).
/// </summary>
public sealed partial class SkyBindingTests
{
    [Fact]
    public void EverySkyBindingOfTheMainWindow_Resolves()
    {
        var xaml = File.ReadAllText(Path.Combine(AccessibilityTests.RepositoryRoot(), "src", "AstroPlanner.UI", "MainWindow.xaml"));
        var paths = SkyPath().Matches(xaml).Select(m => m.Groups[1].Value).Distinct().ToList();

        paths.Should().HaveCountGreaterThan(30, "the sky tab binds most of its controls");
        paths.Where(path => !Resolves(typeof(ShellViewModel), path)).Should().BeEmpty();
    }

    private static bool Resolves(Type type, string path)
    {
        foreach (var name in path.Split('.'))
        {
            if (type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) is not { } property)
            {
                return false;
            }

            type = property.PropertyType;
        }

        return true;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Binding\s+(?:DataContext\.)?(Sky\.[A-Za-z.]+)")]
    private static partial System.Text.RegularExpressions.Regex SkyPath();
}
