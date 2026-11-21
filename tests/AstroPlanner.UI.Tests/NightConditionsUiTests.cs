using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Data.Cache;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>M4a: seeing, transparency, dew and wind in Tonight, the verdicts, the online status and the cache.</summary>
public sealed class NightConditionsUiTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Aguascalientes (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTime SeriesStart = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ObserverSettings Aguascalientes = new()
    {
        CityId = null,
        IsManualLocation = true,
        LatitudeDegrees = 21.88,
        LongitudeDegrees = -102.29,
        ElevationMeters = 1888,
        TimeZoneId = "Central Standard Time (Mexico)",
        MinimumAltitude = 20,
        MaximumMagnitude = 20
    };

    private sealed class FakeWeather(Func<double, double, WeatherForecast> make) : IWeatherForecastService
    {
        public int Calls { get; private set; }

        public Task<WeatherForecast> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(make(latitude, longitude));
        }
    }

    private sealed class FakeAstro(Func<double, double, AstroForecast> make) : IAstroForecastService
    {
        public int Calls { get; private set; }
        public Exception? FailWith { get; set; }

        public Task<AstroForecast> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            Calls++;
            return FailWith is not null ? Task.FromException<AstroForecast>(FailWith) : Task.FromResult(make(latitude, longitude));
        }
    }

    /// <summary>48 hours: clear, the air cooling 1 °C per hour toward a 4 °C dew point, gusts of <paramref name="gust"/>.</summary>
    private static WeatherForecast Weather(double latitude, double longitude, DateTime fetchedUtc, double gust = 10) =>
        new(latitude, longitude,
            [.. Enumerable.Range(0, 48).Select(i => new HourlyWeather(SeriesStart.AddHours(i), 5)
            {
                TemperatureC = Math.Max(5, 34 - i), DewPointC = 4, WindSpeedKmh = gust / 2, WindGustKmh = gust
            })],
            fetchedUtc);

    private static AstroForecast Astro(double latitude, double longitude, DateTime fetchedUtc, int seeing = 4, int transparency = 2) =>
        new(latitude, longitude,
            [.. Enumerable.Range(0, 24).Select(i => new AstroForecastPoint(SeriesStart.AddHours(3 * i), 2, seeing, transparency, 2))],
            fetchedUtc);

    private static async Task<ShellViewModel> CreateAsync(
        IWeatherForecastService? weather, IAstroForecastService? astro, ObserverSettings? settings = null, ManualTimeProvider? clock = null)
    {
        var shell = TestShell.Create(
            catalog: [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13],
            settings: new FakeSettingsService(settings ?? Aguascalientes),
            time: clock ?? new ManualTimeProvider(WinterNightUtc),
            weather: weather,
            astro: astro,
            equipment: new FakeEquipmentRepository(EquipmentInventory.Demo),
            atlas: new FakeAtlas(1.84));
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        await shell.Targets.FeasibilityTask;
        return shell;
    }

    [Fact]
    public async Task Tonight_ShowsSeeingDewAndWind_WithTheirAlerts()
    {
        var now = WinterNightUtc.UtcDateTime;
        var shell = await CreateAsync(
            new FakeWeather((lat, lon) => Weather(lat, lon, now, gust: 35)),
            new FakeAstro((lat, lon) => Astro(lat, lon, now)));

        shell.Tonight.ConditionsText.Should().Be("Seeing 1.1″ · transparency 0.35 mag");
        shell.Tonight.ConditionsDetailText.Should().StartWith("Dew margin 1 °C").And.Contain("gusts up to 35 km/h");
        // The Milky Way core alert is found in the background and may or may not be there yet.
        await shell.Tonight.MilkyWayTask;
        var conditions = shell.Tonight.Alerts.Where(a => !a.StartsWith("🌌", StringComparison.Ordinal)).ToList();
        conditions.Should().HaveCount(2);
        conditions[0].Should().StartWith("⚠ Dew likely from");
        conditions[1].Should().Contain("35 km/h").And.Contain("over 30 km/h", "the demo rig is a camera on a tripod");
        shell.OnlineStatusText.Should().Be(Strings.OnlineStatusOnline);
        shell.OnlineStatusTooltip.Should().Contain("Open-Meteo): updated").And.Contain("7Timer!): updated").And.Contain("GitHub): on");
    }

    [Fact]
    public async Task ForecastSeeingAndHaze_ReachTheVerdicts()
    {
        var now = WinterNightUtc.UtcDateTime;
        var shell = await CreateAsync(null, new FakeAstro((lat, lon) => Astro(lat, lon, now, seeing: 8, transparency: 8)));

        var m42 = shell.Targets.AllObjects.Single(r => r.Id == "M42").Feasibility!;

        m42.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Seeing && r.Value == 2.75);
        m42.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Transparency && r.Impact == ReasonImpact.Negative);
        shell.Targets.SelectById("M42").Should().BeTrue();
        shell.Detail.FeasibilityLines.Should().Contain(l => l.Text.StartsWith("Forecast seeing 2.8″", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeeingDisabled_IsNeverRequested_AndSaysSo()
    {
        var astro = new FakeAstro((lat, lon) => Astro(lat, lon, WinterNightUtc.UtcDateTime));
        var settings = new ObserverSettings
        {
            IsManualLocation = true, LatitudeDegrees = 21.88, LongitudeDegrees = -102.29, ElevationMeters = 1888,
            TimeZoneId = "Central Standard Time (Mexico)", CityId = null, AstroForecastEnabled = false, WeatherForecastEnabled = false,
            SurveyImagesEnabled = false, WikipediaEnabled = false, NameResolverEnabled = false, SmallBodiesEnabled = false, SatellitesEnabled = false
        };

        var shell = await CreateAsync(null, astro, settings);
        await shell.Weather.RefreshAsync(force: true);

        astro.Calls.Should().Be(0);
        shell.Tonight.ConditionsText.Should().Be(Strings.ConditionsAstroDisabled);
        // No dew or wind alert; only the Milky Way core one, found in the background, may be there.
        await shell.Tonight.MilkyWayTask;
        shell.Tonight.Alerts.Should().OnlyContain(a => a.StartsWith("🌌", StringComparison.Ordinal));
        shell.OnlineStatusText.Should().Be(Strings.OnlineStatusOff);
        shell.Targets.AllObjects.Single(r => r.Id == "M42").Feasibility!.Reasons.Should().NotContain(r => r.Factor == FeasibilityFactor.Seeing);
    }

    [Fact]
    public async Task SeeingUnavailable_IsShown_AndNotRetriedOnEveryRecalculation()
    {
        var astro = new FakeAstro((lat, lon) => Astro(lat, lon, WinterNightUtc.UtcDateTime))
        {
            FailWith = new WeatherForecastException("No connection to 7Timer!.")
        };
        var shell = await CreateAsync(null, astro);
        var calls = astro.Calls;

        shell.ShiftObservationTimeCommand.Execute("60");

        astro.Calls.Should().Be(calls);
        shell.Tonight.ConditionsText.Should().Be("Seeing unavailable: No connection to 7Timer!.");
        shell.OnlineStatusText.Should().Be(Strings.OnlineStatusOffline);
        shell.OnlineStatusTooltip.Should().Contain("unavailable (No connection to 7Timer!.)");
    }

    [Fact]
    public async Task OldForecastFromTheCache_IsShownAsOf_AndNotRefetchedInALoop()
    {
        // Offline: the services answer from their disk cache with yesterday's forecast.
        var yesterday = WinterNightUtc.UtcDateTime.AddHours(-20);
        var weather = new FakeWeather((lat, lon) => Weather(lat, lon, yesterday));
        var astro = new FakeAstro((lat, lon) => Astro(lat, lon, yesterday));
        var shell = await CreateAsync(weather, astro);
        var calls = (weather.Calls, astro.Calls);

        shell.ShiftObservationTimeCommand.Execute("60");
        await shell.Weather.RefreshAsync();

        (weather.Calls, astro.Calls).Should().Be(calls, "an offline answer is not retried before the retry delay");
        shell.Weather.IsForecastStale.Should().BeTrue();
        shell.Tonight.ConditionsText.Should().EndWith("(as of 02:00, offline)");
        shell.Tonight.CloudDetailText.Should().EndWith("(as of 02:00, offline)");
        shell.OnlineStatusText.Should().Be(Strings.OnlineStatusOffline);
        shell.OnlineStatusTooltip.Should().Contain("offline, showing the forecast of 02:00");
    }

    [Fact]
    public void Settings_ShowAndClearTheCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", "cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new DiskCache(root);
            cache.Write("open-meteo", "k", new byte[1024 * 1024], DateTime.UtcNow);
            var settings = new SettingsViewModel(new FakeSettingsService(new ObserverSettings()), new FakeCityRepository(), new NullLogger(), cache);

            settings.HasCache.Should().BeTrue();
            settings.CacheSizeText.Should().Be(DisplayNames.Format(Strings.SettingsCacheSizeFormat, (1024 * 1024 + 8) / 1048576.0));

            settings.ClearCacheCommand.Execute(null);

            cache.SizeBytes.Should().Be(0);
            settings.CacheSizeText.Should().Contain("0.0 MB");
            settings.AstroForecastEnabled.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
