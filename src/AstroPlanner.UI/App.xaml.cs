using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Services;
using AstroPlanner.Data.Catalog;
using AstroPlanner.Data.LightPollution;
using AstroPlanner.Data.Repositories;
using AstroPlanner.Data.Storage;
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

        // Error log (one file per day in %LocalAppData%\AstroPlannerPro\logs)
        services.AddSingleton<IAppLogger, FileAppLogger>();

        // Data: deep-sky catalog (embedded catalogs.sqlite, copied to the data folder on first
        // use) + planets, cities
        services.AddSingleton<ICelestialRepository>(new CatalogWithPlanetsRepository(
            SqliteCatalogRepository.FromEmbedded(AppPaths.DataFile("catalogs"))));
        services.AddSingleton<ICityRepository, JsonCityRepository>();
        // Light-pollution grids (World Atlas 2015, embedded; decompressed on first lookup)
        services.AddSingleton<ILightPollutionAtlas>(LightPollutionAtlas.FromEmbedded());

        // Application services (pure Core)
        services.AddSingleton<IAstronomyService, AstronomyService>();

        // Internet: one shared HttpClient with a timeout and identification
        // (GitHub rejects requests without a User-Agent).
        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"AstroPlannerPro/{AppVersion.Current}");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/Yamato6/AstroPlanner-Pro)");
            return http;
        });
        services.AddSingleton<IWeatherForecastService>(sp =>
            new OpenMeteoForecastService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();

        // User data: app.db (settings, locations, plan, logbook), migrated on first use
        services.AddSingleton(_ => new AppDatabase(AppPaths.DataFile("app.db")));
        services.AddSingleton<ISettingsService, SqliteSettingsService>();
        services.AddSingleton<ISkyViewStore, SqliteSkyViewStore>();
        services.AddSingleton<IObservationPlanService, SqlitePlanService>();
        services.AddSingleton<IObservationLogService, SqliteLogbookService>();
        services.AddSingleton<ILocationRepository, SqliteLocationRepository>();
        services.AddSingleton<IEquipmentRepository, SqliteEquipmentRepository>();
        services.AddSingleton(sp => new V1ImportService(
            sp.GetRequiredService<AppDatabase>(), sp.GetRequiredService<ICelestialRepository>(),
            sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<IObservationPlanService>(),
            sp.GetRequiredService<IObservationLogService>(), sp.GetRequiredService<ILocationRepository>(),
            V1ImportService.DefaultV1Directory()));

        // Shared observing state and forecast
        services.AddSingleton<IObserverContext, ObserverContext>();
        services.AddSingleton<WeatherState>();
        services.AddSingleton<EquipmentState>();

        // View models
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogbookViewModel>();
        services.AddSingleton<EquipmentViewModel>();
        services.AddSingleton<LocationsViewModel>();
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
        _logger.LogInfo($"AstroPlanner Pro {AppVersion.Current} started.");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (_singleInstance is null)
        {
            _logger.LogInfo("Could not check whether another instance was already open; continuing anyway.");
        }

        if (!OpenDatabase(_serviceProvider.GetRequiredService<AppDatabase>()))
        {
            Shutdown(1);
            return;
        }

        OfferV1Import(_serviceProvider.GetRequiredService<V1ImportService>());

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _serviceProvider.GetRequiredService<ShellViewModel>();
        mainWindow.RememberPlacement(new WindowPlacementStore());
        mainWindow.Show();

        _singleInstance?.ListenForActivation(() =>
            Dispatcher.BeginInvoke(() => NativeWindows.BringToFront(mainWindow)));
    }

    /// <summary>
    /// Creates or migrates app.db before anything reads it. A damaged file is set aside and the
    /// user is told; a database from a newer version stops the app instead of being changed.
    /// </summary>
    private bool OpenDatabase(AppDatabase database)
    {
        try
        {
            database.Initialize();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogError("Could not open the user database.", ex);
            MessageBox.Show(DisplayNames.Format(Strings.DatabaseOpenFailedFormat, ex.Message),
                Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        if (database.PreservedCorruptFilePath is { } preserved)
        {
            _logger?.LogInfo($"Damaged database set aside as {preserved}.");
            MessageBox.Show(DisplayNames.Format(Strings.DatabaseCorruptFormat, preserved),
                Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return true;
    }

    /// <summary>First run: offers to import AstroPlanner v1's settings, plan and logbook.</summary>
    private void OfferV1Import(V1ImportService import)
    {
        try
        {
            if (!import.ShouldOffer)
            {
                return;
            }

            var answer = MessageBox.Show(Strings.ImportV1Question, Strings.ImportV1Title,
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                import.MarkOffered();
                return;
            }

            // Before any window exists: blocking here is safe (the import does not need the UI thread).
            var result = import.ImportAsync().GetAwaiter().GetResult();
            _logger?.LogInfo($"Imported from AstroPlanner v1: {result.PlanItemsAdded} plan items, {result.LogEntriesAdded} logbook entries.");
            MessageBox.Show(V1ImportService.Describe(result), Strings.ImportV1Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (V1ImportService.IsImportError(ex))
        {
            _logger?.LogError("Could not import the AstroPlanner v1 data.", ex);
            MessageBox.Show(DisplayNames.Format(Strings.ImportV1Failed, ex.Message), Strings.ImportV1Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
