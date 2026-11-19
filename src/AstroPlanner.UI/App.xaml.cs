using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Services;
using AstroPlanner.Data.Repositories;
using AstroPlanner.Online.Weather;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI;

/// <summary>
/// WPF entry point and composition root: Core knows nothing about the UI, and the UI
/// assembles the implementations.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private IAppLogger? _logger;
    private SingleInstance? _singleInstance;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // System clock (replaceable in tests; used by live mode)
        services.AddSingleton(TimeProvider.System);

        // Error log (one file per day in %LocalAppData%\AstroPlanner\logs)
        services.AddSingleton<IAppLogger, FileAppLogger>();

        // Data: Messier catalog + planets, cities
        services.AddSingleton<ICelestialRepository>(
            new CatalogWithPlanetsRepository(new JsonCelestialRepository()));
        services.AddSingleton<ICityRepository, JsonCityRepository>();

        // Application services (pure Core)
        services.AddSingleton<IAstronomyService, AstronomyService>();

        // Internet: one shared HttpClient with a timeout and identification
        // (GitHub rejects requests without a User-Agent).
        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"AstroPlanner/{AppVersion.Current}");
            return http;
        });
        services.AddSingleton<IWeatherForecastService>(sp =>
            new OpenMeteoForecastService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();

        // UI persistence
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IObservationPlanService, JsonObservationPlanService>();
        services.AddSingleton<IObservationLogService>(_ => new JsonObservationLogService());

        // Shared observing state and forecast
        services.AddSingleton<IObserverContext, ObserverContext>();
        services.AddSingleton<WeatherState>();

        // View models
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogbookViewModel>();
        services.AddSingleton<TargetsViewModel>();
        services.AddSingleton<SessionPlanViewModel>();
        services.AddSingleton<ObjectDetailViewModel>();
        services.AddSingleton<SkyViewModel>();
        services.AddSingleton<TonightViewModel>();
        services.AddSingleton<ShellViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (_serviceProvider is null) return;

        // If a window is already open, bring it to the front and exit this instance.
        _singleInstance = SingleInstance.TryCreate();
        if (_singleInstance is { IsFirstInstance: false })
        {
            _singleInstance.ActivateFirstInstance();
            Shutdown();
            return;
        }

        _logger = _serviceProvider.GetRequiredService<IAppLogger>();
        _logger.LogInfo($"AstroPlanner {AppVersion.Current} started.");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (_singleInstance is null)
        {
            _logger.LogInfo("Could not check whether another instance was already open; continuing anyway.");
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _serviceProvider.GetRequiredService<ShellViewModel>();
        mainWindow.RememberPlacement(new WindowPlacementStore());
        mainWindow.Show();

        _singleInstance?.ListenForActivation(() =>
            Dispatcher.BeginInvoke(() => NativeWindows.BringToFront(mainWindow)));
    }

    /// <summary>
    /// Unhandled error on the UI thread: logged, the user is told, and the application stays
    /// open instead of closing without explanation.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError("Unhandled error in the user interface.", e.Exception);
        MessageBox.Show(
            DisplayNames.Format(Strings.UnhandledErrorMessage, _logger?.LogLocation),
            Strings.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>Fatal error outside the UI thread: it can only be logged.</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _logger?.LogError("Fatal unhandled error.", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError("Unobserved error in a background task.", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
