using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Services;
using AstroPlanner.Data.Cache;
using AstroPlanner.Data.Catalog;
using AstroPlanner.Data.LightPollution;
using AstroPlanner.Data.Repositories;
using AstroPlanner.Data.Storage;
using AstroPlanner.Online.Imaging;
using AstroPlanner.Online.Info;
using AstroPlanner.Online.Satellites;
using AstroPlanner.Online.SmallBodies;
using AstroPlanner.Online.Weather;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.Sky;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.UI;

/// <summary>
/// WPF entry point and composition root: Core knows nothing about the UI, and the UI
/// assembles the implementations.
/// </summary>
public partial class App : System.Windows.Application
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

        // Internet (spec §9): one named HttpClient per service with identification (GitHub rejects
        // requests without a User-Agent), a 15 s budget and the standard resilience pipeline
        // (retries with backoff, circuit breaker). Answers are cached on disk for offline use.
        services.AddSingleton<IOnlineCache>(_ => new DiskCache(AppPaths.DataFile("cache")));
        foreach (var name in OnlineClients.All)
        {
            services.AddHttpClient(name, http =>
                {
                    http.Timeout = Timeout.InfiniteTimeSpan;   // the resilience pipeline enforces it
                    http.DefaultRequestHeaders.UserAgent.ParseAdd($"AstroPlannerPro/{AppBuild.Version}");
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/Yamato6/AstroPlanner-Pro)");
                })
                .AddStandardResilienceHandler(options =>
                {
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(6);
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                    options.Retry.MaxRetryAttempts = 2;
                });
        }

        // CelesTrak blocks addresses that repeat failing requests: no retries at all, and the
        // service itself pauses for 2 h after an error (CelesTrakService).
        services.AddHttpClient(OnlineClients.CelesTrak, http =>
            {
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"AstroPlannerPro/{AppBuild.Version}");
                http.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/Yamato6/AstroPlanner-Pro)");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(40);
                options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
            });

        services.AddSingleton<IWeatherForecastService>(sp => new OpenMeteoForecastService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.OpenMeteo), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOnlineCache>()));
        services.AddSingleton<IAstroForecastService>(sp => new SevenTimerAstroService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.SevenTimer), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOnlineCache>()));
        services.AddSingleton<ISurveyTileSource>(sp => new HipsTileService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.Hips), sp.GetRequiredService<IOnlineCache>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IObjectInfoService>(sp => new WikipediaSummaryService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.Wikipedia), sp.GetRequiredService<IOnlineCache>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<INameResolver>(sp => new SesameResolver(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.Sesame), sp.GetRequiredService<IOnlineCache>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ISmallBodyService>(sp => new MpcSmallBodyService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.Mpc), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOnlineCache>()));
        services.AddSingleton<SmallBodyState>();
        services.AddSingleton<ISatelliteService>(sp => new CelesTrakService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.CelesTrak), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOnlineCache>()));
        services.AddSingleton<SatelliteState>();
        services.AddSingleton<IImageDecoder, WpfImageDecoder>();
        services.AddSingleton<OnlineInfoState>();
        services.AddSingleton<IUpdateChecker>(sp => new GitHubUpdateChecker(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OnlineClients.GitHub), sp.GetRequiredService<IAppLogger>()));

        // User data: app.db (settings, locations, plan, logbook), migrated on first use
        services.AddSingleton(_ => new AppDatabase(AppPaths.DataFile("app.db")));
        services.AddSingleton<ISettingsService, SqliteSettingsService>();
        services.AddSingleton<ISkyViewStore, SqliteSkyViewStore>();
        services.AddSingleton<IInterfaceStore, SqliteInterfaceStore>();
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
        services.AddSingleton<ThemeState>();
        services.AddSingleton(sp => MediaLibrary.Load(AppPaths.MediaDirectory, sp.GetRequiredService<IAppLogger>()));

        // View models
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogbookViewModel>();
        services.AddSingleton<EquipmentViewModel>();
        services.AddSingleton<LocationsViewModel>();
        services.AddSingleton<TargetsViewModel>();
        services.AddSingleton<SessionPlanViewModel>();
        services.AddSingleton<FramingViewModel>();
        services.AddSingleton<ObjectInfoViewModel>();
        services.AddSingleton<ObjectDetailViewModel>();
        services.AddSingleton<SkyViewModel>();
        services.AddSingleton<TonightViewModel>();
        services.AddSingleton<ShellViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
        services.AddSingleton<WebShellWindow>();
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
        _logger.LogInfo($"AstroPlanner Pro {AppBuild.Version} started.");
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

        // The saved theme before the first window is created (SettingsViewModel loads it).
        var theme = _serviceProvider.GetRequiredService<ThemeState>();
        _ = _serviceProvider.GetRequiredService<SettingsViewModel>();
        ThemeManager.Apply(this, theme.Current);
        theme.Changed += (_, _) => ThemeManager.Apply(this, theme.Current);

        // --ui=web: the v3 planetarium UI (in development until v3.0.0) instead of the v2 tabs.
        Window mainWindow;
        if (SkyLaunchOptions.UseWebUi(Environment.GetCommandLineArgs()))
        {
            _logger.LogInfo("Starting the planetarium UI (--ui=web).");
            mainWindow = _serviceProvider.GetRequiredService<WebShellWindow>();
        }
        else
        {
            var classicWindow = _serviceProvider.GetRequiredService<MainWindow>();
            classicWindow.RememberPlacement(new WindowPlacementStore());
            mainWindow = classicWindow;
        }

        mainWindow.DataContext = _serviceProvider.GetRequiredService<ShellViewModel>();
        // Startup time (spec §14 budget): process start → first frame of the window (Tonight).
        mainWindow.ContentRendered += (_, _) => _logger.LogInfo(string.Create(CultureInfo.InvariantCulture,
            $"Window shown {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms after the process started."));
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
