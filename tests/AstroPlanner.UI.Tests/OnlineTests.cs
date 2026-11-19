using System.Net;
using System.Text;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;
using FluentAssertions;

namespace AstroPlanner.UI.Tests;

/// <summary>
/// Funciones de la 0.8: pronóstico de nubes (Open-Meteo) y aviso de versión
/// nueva (GitHub), sin red: las respuestas HTTP son simuladas.
/// </summary>
public sealed class OnlineTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 en CDMX (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    // ─── Dobles de prueba ───────────────────────────────────────────────

    /// <summary>Como el servicio real, devuelve el pronóstico de las coordenadas pedidas.</summary>
    private sealed class CountingWeatherService(Func<double, double, WeatherForecast> make) : IWeatherForecastService
    {
        public int Calls { get; private set; }
        public Exception? FailWith { get; set; }

        public Task<WeatherForecast> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            Calls++;
            return FailWith is not null
                ? Task.FromException<WeatherForecast>(FailWith)
                : Task.FromResult(make(latitude, longitude));
        }
    }

    private sealed class FixedUpdateChecker(UpdateInfo? result) : IUpdateChecker
    {
        public int Calls { get; private set; }

        public Task<UpdateInfo?> CheckAsync(AppVersion current, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>Pronóstico para CDMX: despejado de 06 a 10 UTC (00-04 local), nublado el resto.</summary>
    private static WeatherForecast CdmxForecast(DateTime fetchedAtUtc, double latitude = 19.4326, double longitude = -99.1332)
    {
        var start = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var hours = Enumerable.Range(0, 48)
            .Select(i => new HourlyWeather(start.AddHours(i), i is >= 30 and < 34 ? 5 : 80))
            .ToList();
        return new WeatherForecast(latitude, longitude, hours, fetchedAtUtc);
    }

    private static MainViewModel CreateMain(
        ManualTimeProvider clock, IWeatherForecastService? weather, IUpdateChecker? updates = null,
        ObserverSettings? settings = null, FakePlanService? plan = null) =>
        new(
            new FakeCelestialRepository([TestCatalog.M42, TestCatalog.M31, TestCatalog.M13]),
            new AstronomyService(),
            plan ?? new FakePlanService(),
            new NullLogger(),
            new SettingsViewModel(new FakeSettingsService(settings ?? new ObserverSettings()), new FakeCityRepository(), new NullLogger()),
            new LogbookViewModel(new FakeLogService(), new NullLogger()),
            clock,
            weather,
            updates);

    // ─── Pronóstico en el ViewModel ─────────────────────────────────────

    [Fact]
    public async Task Night_ShowsCloudSummaryAndPlanCloudsAtCulmination()
    {
        var clock = new ManualTimeProvider(WinterNightUtc);
        var weather = new CountingWeatherService((lat, lon) => CdmxForecast(clock.UtcNow.UtcDateTime, lat, lon));
        var plan = new FakePlanService { Entries = [new("M42")] };
        var viewModel = CreateMain(clock, weather, plan: plan);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.CloudCoverText.Should().MatchRegex(@"^\d+ %$");
        viewModel.CloudDetailText.Should().Contain("mejor 00:00").And.Contain("(5 %)");
        viewModel.GetNightCloudCover().Should().NotBeEmpty()
            .And.OnlyContain(h => h.StartLocal >= viewModel.Night!.WindowStartLocal && h.EndLocal <= viewModel.Night.WindowEndLocal);

        var m42 = viewModel.ObservationPlan.Single();
        m42.CloudCoverAtCulmination.Should().Be(80, "M42 culmina hacia las 22:20 local (04:20 UTC), hora nublada");
        viewModel.SelectedObservation!.CloudCoverAtCulmination.Should().NotBeNull();
    }

    [Fact]
    public async Task Forecast_IsCachedForAnHourAndRefetchedWhenTheLocationChanges()
    {
        var clock = new ManualTimeProvider(WinterNightUtc);
        var weather = new CountingWeatherService((lat, lon) => CdmxForecast(clock.UtcNow.UtcDateTime, lat, lon));
        var viewModel = CreateMain(clock, weather);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var callsAfterLoad = weather.Calls;

        // Cambiar la hora o filtrar no vuelve a consultar.
        viewModel.ShiftObservationTimeCommand.Execute("60");
        viewModel.SearchText = "m42";
        weather.Calls.Should().Be(callsAfterLoad);

        // Pasada una hora, sí.
        clock.UtcNow = clock.UtcNow.AddMinutes(61);
        await viewModel.RefreshWeatherAsync();
        weather.Calls.Should().Be(callsAfterLoad + 1);

        // Otra ubicación (más de ~1 km): se pide el pronóstico nuevo al recalcular,
        // y después vuelve a usarse la caché.
        viewModel.Latitude = 20.6597;
        weather.Calls.Should().Be(callsAfterLoad + 2);
        await viewModel.RefreshWeatherAsync();
        weather.Calls.Should().Be(callsAfterLoad + 2);
    }

    [Fact]
    public async Task Failure_IsShownAndNotRetriedOnEveryRecalculation()
    {
        var clock = new ManualTimeProvider(WinterNightUtc);
        var weather = new CountingWeatherService((lat, lon) => CdmxForecast(clock.UtcNow.UtcDateTime, lat, lon))
        {
            FailWith = new WeatherForecastException("Sin conexión con el servicio del tiempo.")
        };
        var viewModel = CreateMain(clock, weather);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.CloudCoverText.Should().Be("—");
        viewModel.CloudDetailText.Should().Contain("Sin conexión");
        var calls = weather.Calls;

        viewModel.ShiftObservationTimeCommand.Execute("60");
        weather.Calls.Should().Be(calls, "tras un fallo se espera antes de reintentar");

        weather.FailWith = null;
        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        await viewModel.RefreshWeatherAsync();
        viewModel.CloudCoverText.Should().EndWith("%");
    }

    [Fact]
    public async Task Disabled_NeverContactsTheServiceAndSaysSo()
    {
        var clock = new ManualTimeProvider(WinterNightUtc);
        var weather = new CountingWeatherService((lat, lon) => CdmxForecast(clock.UtcNow.UtcDateTime, lat, lon));
        var viewModel = CreateMain(clock, weather, settings: new ObserverSettings { WeatherForecastEnabled = false });
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.RefreshWeatherAsync(force: true);

        weather.Calls.Should().Be(0);
        viewModel.CloudDetailText.Should().Contain("desactivado");
        viewModel.GetNightCloudCover().Should().BeEmpty();
    }

    // ─── Aviso de versión nueva ─────────────────────────────────────────

    private const string ReleasesJson = """
        [
          {"tag_name":"v0.9.0","name":"AstroPlanner 0.9","html_url":"https://github.com/Yamato6/AstroPlanner/releases/tag/v0.9.0","draft":true},
          {"tag_name":"v0.8.1","name":"AstroPlanner 0.8.1","html_url":"https://github.com/Yamato6/AstroPlanner/releases/tag/v0.8.1","draft":false},
          {"tag_name":"v0.8.0","name":"AstroPlanner 0.8","html_url":"https://github.com/Yamato6/AstroPlanner/releases/tag/v0.8.0","draft":false},
          {"tag_name":"PreRelease","name":"AstroPlanner_0.2","html_url":"https://github.com/Yamato6/AstroPlanner/releases/tag/PreRelease","draft":false}
        ]
        """;

    [Theory]
    [InlineData("0.8.0.0", "0.8.1")]   // hay una más nueva (el borrador 0.9 no cuenta)
    [InlineData("0.8.1.0", null)]      // ya es la última
    [InlineData("1.0.0.0", null)]      // más nueva que todas
    public async Task UpdateChecker_FindsTheNewestPublishedVersion(string current, string? expected)
    {
        var checker = new GitHubUpdateChecker(new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json(ReleasesJson))), new NullLogger());

        var update = await checker.CheckAsync(AppVersion.Parse(current));

        update?.Version.ToString().Should().Be(expected);
        if (expected is null) update.Should().BeNull();
        else update!.Url.Should().EndWith("/v0.8.1");
    }

    [Fact]
    public async Task UpdateChecker_PrivateRepoOrNoNetwork_ReturnsNullWithoutThrowing()
    {
        var notFound = new GitHubUpdateChecker(
            new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound))), new NullLogger());
        var offline = new GitHubUpdateChecker(
            new HttpClient(new StubHttpHandler(_ => throw new HttpRequestException("sin red"))), new NullLogger());

        (await notFound.CheckAsync(AppVersion.Parse("0.8.0"))).Should().BeNull();
        (await offline.CheckAsync(AppVersion.Parse("0.8.0"))).Should().BeNull();
    }

    [Theory]
    [InlineData("v0.8.0", "0.8.0")]
    [InlineData("0.10.2", "0.10.2")]
    [InlineData("v1.0", "1.0.0")]
    [InlineData("PreRelease", null)]
    [InlineData("Pre_0.3", null)]
    public void ParseTag_HandlesVersionTagsAndIgnoresOthers(string tag, string? expected)
    {
        GitHubUpdateChecker.ParseTag(tag)?.ToString().Should().Be(expected);
        if (expected is null) GitHubUpdateChecker.ParseTag(tag).Should().BeNull();
    }

    [Fact]
    public async Task UpdateBanner_ShowsWhenEnabledAndCanBeDismissed()
    {
        var update = new UpdateInfo(AppVersion.Parse("0.9.0"), "AstroPlanner 0.9", "https://github.com/Yamato6/AstroPlanner/releases/tag/v0.9.0");
        var checker = new FixedUpdateChecker(update);
        var viewModel = CreateMain(new ManualTimeProvider(WinterNightUtc), null, checker);

        await viewModel.CheckForUpdatesAsync(AppVersion.Parse("0.8.0"));

        viewModel.HasUpdate.Should().BeTrue();
        viewModel.UpdateBannerText.Should().Contain("AstroPlanner 0.9");
        viewModel.DismissUpdateCommand.Execute(null);
        viewModel.HasUpdate.Should().BeFalse();

        var disabled = CreateMain(new ManualTimeProvider(WinterNightUtc), null, checker,
            settings: new ObserverSettings { CheckForUpdates = false });
        await disabled.CheckForUpdatesAsync(AppVersion.Parse("0.8.0"));
        disabled.HasUpdate.Should().BeFalse();
        checker.Calls.Should().Be(1, "con la opción desactivada no se consulta GitHub");
    }
}
