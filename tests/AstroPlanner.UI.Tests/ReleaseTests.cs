using System.Globalization;
using System.IO;
using System.Windows;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Converters;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.Tests;

/// <summary>Versions with RCs, links, window position and user-facing names.</summary>
public class ReleaseTests
{
    // ─── Versions ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("v1.0.0-rc.1", "1.0.0", "rc.1")]
    [InlineData("1.0.0-rc.1+3f2a9c1", "1.0.0", "rc.1")]   // SDK build metadata
    [InlineData("0.8", "0.8.0", null)]
    [InlineData("1.0.0.0", "1.0.0", null)]
    [InlineData("v3.0.0-alpha.1", "3.0.0", "alpha.1")]
    public void AppVersion_ParsesReleaseAndPreReleaseTags(string text, string number, string? preRelease)
    {
        var version = AppVersion.Parse(text);

        version.Number.Should().Be(Version.Parse(number));
        version.PreRelease.Should().Be(preRelease);
        version.IsPreRelease.Should().Be(preRelease is not null);
    }

    [Theory]
    [InlineData("1.0.0-rc.1", "1.0.0")]        // the RC comes before the final release
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10")]  // numeric identifiers compare as numbers
    [InlineData("1.0.0-beta", "1.0.0-rc.1")]
    [InlineData("0.8.0", "1.0.0-rc.1")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("2.0.1", "3.0.0-alpha.1")]         // the v3 preview is newer than v2
    [InlineData("3.0.0-alpha.1", "3.0.0-beta.1")]
    public void AppVersion_OrdersLikeSemver(string lower, string higher)
    {
        (AppVersion.Parse(lower) < AppVersion.Parse(higher)).Should().BeTrue();
        (AppVersion.Parse(higher) > AppVersion.Parse(lower)).Should().BeTrue();
    }

    [Fact]
    public void AppVersion_Current_ComesFromTheBuild()
    {
        AppBuild.Version.Number.Should().BeGreaterThanOrEqualTo(new Version(1, 0, 0));
        AppBuild.Version.ToString().Should().NotContain("+", "build metadata is not shown");
    }

    private static UpdateInfo? Newest(string current, params (string Tag, bool PreRelease)[] releases) =>
        GitHubUpdateChecker.Newest(
            releases.Select(r => new GitHubUpdateChecker.GitHubRelease
            {
                TagName = r.Tag,
                Name = "AstroPlanner " + r.Tag,
                HtmlUrl = "https://github.com/Yamato6/AstroPlanner/releases/tag/" + r.Tag,
                PreRelease = r.PreRelease
            }),
            AppVersion.Parse(current));

    [Fact]
    public void UpdateChecker_OffersTheReleaseCandidateTo0xUsers()
    {
        Newest("0.8.0", ("v0.8.0", true), ("v1.0.0-rc.1", true))!
            .Version.ToString().Should().Be("1.0.0-rc.1");
    }

    [Fact]
    public void UpdateChecker_OffersTheFinalReleaseToReleaseCandidateUsers()
    {
        Newest("1.0.0-rc.1", ("v1.0.0-rc.1", true), ("v1.0.0", false))!
            .Version.ToString().Should().Be("1.0.0");
    }

    [Fact]
    public void UpdateChecker_StableUsersNeverGetPreReleases()
    {
        // Neither by the tag suffix nor by GitHub's "pre-release" flag.
        Newest("1.0.0", ("v1.1.0-rc.1", false), ("v1.0.5", true)).Should().BeNull();
        Newest("1.0.0", ("v1.1.0-rc.1", false), ("v1.0.1", false))!
            .Version.ToString().Should().Be("1.0.1");
    }

    // ─── Links ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://github.com/Yamato6/AstroPlanner/releases/tag/v1.0.0", true)]
    [InlineData("https://open-meteo.com/", true)]
    [InlineData("https://api.open-meteo.com/v1/forecast", true)]
    [InlineData("http://github.com/Yamato6/AstroPlanner", false)]     // no https
    [InlineData("https://github.com.evil.example/", false)]           // look-alike domain
    [InlineData("https://evilgithub.com/", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    public void SafeLinks_OnlyAllowsHttpsLinksToKnownSites(string url, bool allowed)
    {
        SafeLinks.IsAllowed(new Uri(url)).Should().Be(allowed);
    }

    // ─── Window position ────────────────────────────────────────────────

    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Rect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void WindowPlacement_KeepsAPositionThatIsOnScreen()
    {
        var saved = new WindowPlacement(100, 80, 1400, 800, false);

        saved.FitTo(Screen, WorkArea, 1080, 640).Should().Be(saved);
    }

    [Fact]
    public void WindowPlacement_CentersAWindowLeftOnADisconnectedMonitor()
    {
        // It was on a second monitor to the right that no longer exists.
        var fitted = new WindowPlacement(2500, 100, 1400, 800, true).FitTo(Screen, WorkArea, 1080, 640);

        fitted.Left.Should().Be(260);
        fitted.Top.Should().Be(120);
        fitted.IsMaximized.Should().BeTrue();
    }

    [Fact]
    public void WindowPlacement_ShrinksToTheDesktopAndRespectsTheMinimumSize()
    {
        new WindowPlacement(0, 0, 3000, 2000, false).FitTo(Screen, WorkArea, 1080, 640)
            .Should().Match<WindowPlacement>(p => p.Width == 1920 && p.Height == 1080);
        new WindowPlacement(0, 0, 300, 200, false).FitTo(Screen, WorkArea, 1080, 640)
            .Should().Match<WindowPlacement>(p => p.Width == 1080 && p.Height == 640);
    }

    [Fact]
    public void WindowPlacement_KeepsALargeWindowOnALargerSecondMonitor()
    {
        // Primary 1920×1080 and, to its right, a second 2560×1440 monitor.
        var desktop = new Rect(0, 0, 1920 + 2560, 1440);
        var saved = new WindowPlacement(2000, 20, 2400, 1300, false);

        saved.FitTo(desktop, WorkArea, 1080, 640).Should().Be(saved);
    }

    [Fact]
    public void WindowPlacementStore_RoundTripsAndIgnoresCorruptFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"astroplanner-window-{Guid.NewGuid():N}.json");
        try
        {
            var store = new WindowPlacementStore(path);
            store.Load().Should().BeNull();

            var placement = new WindowPlacement(10, 20, 1300, 700, true);
            store.Save(placement);
            store.Load().Should().Be(placement);

            File.WriteAllText(path, "{ not json");
            store.Load().Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ─── User-facing names ──────────────────────────────────────────────

    [Fact]
    public void DisplayNames_EveryObjectTypeHasAReadableName()
    {
        var names = Enum.GetValues<CelestialObjectType>().Select(DisplayNames.Of).ToList();

        names.Should().OnlyContain(name => !string.IsNullOrWhiteSpace(name)).And.OnlyHaveUniqueItems();
        names.Should().NotContain(name => name.Contains("Cluster", StringComparison.Ordinal) && !name.Contains(' ', StringComparison.Ordinal),
            "internal PascalCase names such as GlobularCluster must not leak into the UI");

        DisplayNames.Of(CelestialObjectType.GlobularCluster).Should().Be("Globular cluster");
    }

    [Fact]
    public void DisplayNameConverter_ShowsObjectTypeNames()
    {
        new DisplayNameConverter().Convert(CelestialObjectType.OpenCluster, typeof(string), null!, CultureInfo.InvariantCulture)
            .Should().Be("Open cluster");
    }

    [Theory]
    [InlineData(5, "Clear")]
    [InlineData(30, "Mostly clear")]
    [InlineData(50, "Partly cloudy")]
    [InlineData(80, "Mostly cloudy")]
    [InlineData(95, "Overcast")]
    public void DisplayNames_DescribeCloudCover(double percent, string expected)
    {
        DisplayNames.CloudCover(percent).Should().Be(expected);
    }

    [Fact]
    public void Strings_EveryResourceIsAvailableAndNotEmpty()
    {
        var properties = typeof(AstroPlanner.Application.Resources.Strings)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .ToList();

        properties.Should().HaveCountGreaterThan(200);
        properties.Should().OnlyContain(p => !string.IsNullOrWhiteSpace((string?)p.GetValue(null)));
    }

    // ─── Release notes ──────────────────────────────────────────────────

    [Fact]
    public void Changelog_HasTheSectionOfThisVersion_ForTheReleaseNotes()
    {
        // tools/release-notes.ps1 publishes this section; the release job fails without it.
        var changelog = File.ReadAllLines(Path.Combine(AccessibilityTests.RepositoryRoot(), "CHANGELOG.md"));
        var heading = $"## [{AppBuild.Version.Number.ToString(3)}]";

        if (AppBuild.Version.PreRelease is "dev")
        {
            return;   // a development build has no section yet
        }

        // Like the script: a pre-release (3.0.0-alpha.1) has its own section, or uses the final version's.
        var own = $"## [{AppBuild.Version}]";
        var start = Array.FindIndex(changelog, line => line.StartsWith(own, StringComparison.Ordinal));
        if (start < 0)
        {
            start = Array.FindIndex(changelog, line => line.StartsWith(heading, StringComparison.Ordinal));
        }

        start.Should().BeGreaterThan(0, $"CHANGELOG.md needs a '{own}' or '{heading}' section");
        changelog.Skip(start + 1).TakeWhile(line => !line.StartsWith("## [", StringComparison.Ordinal))
            .Should().Contain(line => line.StartsWith("### ", StringComparison.Ordinal));
    }
}
