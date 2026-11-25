using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AstroPlanner.SkyBridge.App;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AppInfoRequest))]
[JsonSerializable(typeof(AppInfoResponse))]
[JsonSerializable(typeof(ObserverGetRequest))]
[JsonSerializable(typeof(ObserverSnapshot))]
[JsonSerializable(typeof(ObserverChangedEvent))]
[JsonSerializable(typeof(NoParameters))]
[JsonSerializable(typeof(SkySnapshot))]
[JsonSerializable(typeof(SkyStepRequest))]
[JsonSerializable(typeof(SkySetRateRequest))]
[JsonSerializable(typeof(SkySetLayerRequest))]
[JsonSerializable(typeof(SkySetViewModeRequest))]
[JsonSerializable(typeof(SkyChangedEvent))]
[JsonSerializable(typeof(ThemeChangedEvent))]
[JsonSerializable(typeof(UiSnapshot))]
[JsonSerializable(typeof(UiSetThemeRequest))]
[JsonSerializable(typeof(SwitchRequest))]
[JsonSerializable(typeof(UiChangedEvent))]
[JsonSerializable(typeof(LayoutSnapshot))]
[JsonSerializable(typeof(AboutResponse))]
[JsonSerializable(typeof(OpenLinkRequest))]
[JsonSerializable(typeof(OpenLinkResponse))]
[JsonSerializable(typeof(SkySetTimeRequest))]
[JsonSerializable(typeof(NightTimeline))]
[JsonSerializable(typeof(NightChangedEvent))]
[JsonSerializable(typeof(SelectionSnapshot))]
[JsonSerializable(typeof(SelectionChangedEvent))]
[JsonSerializable(typeof(SearchQueryRequest))]
[JsonSerializable(typeof(SearchResults))]
[JsonSerializable(typeof(SearchGoRequest))]
[JsonSerializable(typeof(SearchResolveRequest))]
[JsonSerializable(typeof(SearchResolveResponse))]
[JsonSerializable(typeof(RpcErrorPayload))]
public sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>A method the web UI can call: its name and the contracts of its request and response.</summary>
public interface IRpcMethod
{
    string Name { get; }
    JsonTypeInfo Request { get; }
    JsonTypeInfo Response { get; }
}

/// <inheritdoc cref="IRpcMethod"/>
public sealed record RpcMethod<TRequest, TResponse>(string Name, JsonTypeInfo<TRequest> Request, JsonTypeInfo<TResponse> Response) : IRpcMethod
{
    JsonTypeInfo IRpcMethod.Request => Request;
    JsonTypeInfo IRpcMethod.Response => Response;
}

/// <summary>An event the host sends to the web UI: its name and payload contract.</summary>
public interface IAppEvent
{
    string Name { get; }
    JsonTypeInfo Payload { get; }
}

/// <inheritdoc cref="IAppEvent"/>
public sealed record AppEvent<TPayload>(string Name, JsonTypeInfo<TPayload> Payload) : IAppEvent
{
    JsonTypeInfo IAppEvent.Payload => Payload;
}

/// <summary>Every method of the app channel. Names are "area.verb", lower camel case.</summary>
public static class AppMethods
{
    public static RpcMethod<AppInfoRequest, AppInfoResponse> AppInfo { get; } =
        new("app.info", AppJsonContext.Default.AppInfoRequest, AppJsonContext.Default.AppInfoResponse);

    public static RpcMethod<ObserverGetRequest, ObserverSnapshot> ObserverGet { get; } =
        new("observer.get", AppJsonContext.Default.ObserverGetRequest, AppJsonContext.Default.ObserverSnapshot);

    public static RpcMethod<NoParameters, SkySnapshot> SkyGet { get; } = Sky("sky.get");
    public static RpcMethod<NoParameters, SkySnapshot> SkyPlay { get; } = Sky("sky.play");
    public static RpcMethod<NoParameters, SkySnapshot> SkyPause { get; } = Sky("sky.pause");
    public static RpcMethod<NoParameters, SkySnapshot> SkyNow { get; } = Sky("sky.now");

    public static RpcMethod<SkyStepRequest, SkySnapshot> SkyStep { get; } =
        new("sky.step", AppJsonContext.Default.SkyStepRequest, AppJsonContext.Default.SkySnapshot);

    public static RpcMethod<SkySetRateRequest, SkySnapshot> SkySetRate { get; } =
        new("sky.setRate", AppJsonContext.Default.SkySetRateRequest, AppJsonContext.Default.SkySnapshot);

    public static RpcMethod<SkySetLayerRequest, SkySnapshot> SkySetLayer { get; } =
        new("sky.setLayer", AppJsonContext.Default.SkySetLayerRequest, AppJsonContext.Default.SkySnapshot);

    public static RpcMethod<SkySetViewModeRequest, SkySnapshot> SkySetViewMode { get; } =
        new("sky.setViewMode", AppJsonContext.Default.SkySetViewModeRequest, AppJsonContext.Default.SkySnapshot);

    public static RpcMethod<NoParameters, AboutResponse> AppAbout { get; } =
        new("app.about", AppJsonContext.Default.NoParameters, AppJsonContext.Default.AboutResponse);

    public static RpcMethod<OpenLinkRequest, OpenLinkResponse> AppOpenLink { get; } =
        new("app.openLink", AppJsonContext.Default.OpenLinkRequest, AppJsonContext.Default.OpenLinkResponse);

    public static RpcMethod<NoParameters, NoParameters> AppOpenDataFolder { get; } =
        new("app.openDataFolder", AppJsonContext.Default.NoParameters, AppJsonContext.Default.NoParameters);

    public static RpcMethod<SkySetTimeRequest, SkySnapshot> SkySetTime { get; } =
        new("sky.setTime", AppJsonContext.Default.SkySetTimeRequest, AppJsonContext.Default.SkySnapshot);

    public static RpcMethod<NoParameters, NightTimeline> NightGet { get; } =
        new("night.get", AppJsonContext.Default.NoParameters, AppJsonContext.Default.NightTimeline);

    public static RpcMethod<NoParameters, SelectionSnapshot> SelectionGet { get; } = Selection("selection.get");
    public static RpcMethod<NoParameters, SelectionSnapshot> SelectionCenter { get; } = Selection("selection.center");
    public static RpcMethod<NoParameters, SelectionSnapshot> SelectionFrame { get; } = Selection("selection.frame");
    public static RpcMethod<NoParameters, SelectionSnapshot> SelectionAddToPlan { get; } = Selection("selection.addToPlan");
    public static RpcMethod<NoParameters, SelectionSnapshot> SelectionRemoveFromPlan { get; } = Selection("selection.removeFromPlan");
    public static RpcMethod<NoParameters, SelectionSnapshot> SelectionClear { get; } = Selection("selection.clear");

    public static RpcMethod<SearchQueryRequest, SearchResults> SearchQuery { get; } =
        new("search.query", AppJsonContext.Default.SearchQueryRequest, AppJsonContext.Default.SearchResults);

    public static RpcMethod<SearchGoRequest, SelectionSnapshot> SearchGo { get; } =
        new("search.go", AppJsonContext.Default.SearchGoRequest, AppJsonContext.Default.SelectionSnapshot);

    public static RpcMethod<SearchResolveRequest, SearchResolveResponse> SearchResolve { get; } =
        new("search.resolve", AppJsonContext.Default.SearchResolveRequest, AppJsonContext.Default.SearchResolveResponse);

    public static RpcMethod<NoParameters, UiSnapshot> UiGet { get; } =
        new("ui.get", AppJsonContext.Default.NoParameters, AppJsonContext.Default.UiSnapshot);

    public static RpcMethod<UiSetThemeRequest, UiSnapshot> UiSetTheme { get; } =
        new("ui.setTheme", AppJsonContext.Default.UiSetThemeRequest, AppJsonContext.Default.UiSnapshot);

    public static RpcMethod<SwitchRequest, UiSnapshot> UiSetReduceTransparency { get; } =
        new("ui.setReduceTransparency", AppJsonContext.Default.SwitchRequest, AppJsonContext.Default.UiSnapshot);

    public static RpcMethod<SwitchRequest, UiSnapshot> UiSetAutoHide { get; } =
        new("ui.setAutoHide", AppJsonContext.Default.SwitchRequest, AppJsonContext.Default.UiSnapshot);

    public static RpcMethod<SwitchRequest, UiSnapshot> UiSetFullScreen { get; } =
        new("ui.setFullScreen", AppJsonContext.Default.SwitchRequest, AppJsonContext.Default.UiSnapshot);

    public static RpcMethod<NoParameters, LayoutSnapshot> LayoutLoad { get; } =
        new("layout.load", AppJsonContext.Default.NoParameters, AppJsonContext.Default.LayoutSnapshot);

    public static RpcMethod<LayoutSnapshot, NoParameters> LayoutSave { get; } =
        new("layout.save", AppJsonContext.Default.LayoutSnapshot, AppJsonContext.Default.NoParameters);

    public static IReadOnlyList<IRpcMethod> All { get; } =
    [
        AppInfo, AppAbout, AppOpenLink, AppOpenDataFolder, ObserverGet,
        SkyGet, SkyPlay, SkyPause, SkyNow, SkyStep, SkySetRate, SkySetLayer, SkySetViewMode, SkySetTime,
        NightGet, SelectionGet, SelectionCenter, SelectionFrame, SelectionAddToPlan, SelectionRemoveFromPlan, SelectionClear,
        SearchQuery, SearchGo, SearchResolve,
        UiGet, UiSetTheme, UiSetReduceTransparency, UiSetAutoHide, UiSetFullScreen, LayoutLoad, LayoutSave,
    ];

    private static RpcMethod<NoParameters, SelectionSnapshot> Selection(string name) =>
        new(name, AppJsonContext.Default.NoParameters, AppJsonContext.Default.SelectionSnapshot);

    private static RpcMethod<NoParameters, SkySnapshot> Sky(string name) =>
        new(name, AppJsonContext.Default.NoParameters, AppJsonContext.Default.SkySnapshot);
}

/// <summary>Every event of the app channel.</summary>
public static class AppEvents
{
    public static AppEvent<ThemeChangedEvent> ThemeChanged { get; } =
        new("theme.changed", AppJsonContext.Default.ThemeChangedEvent);

    public static AppEvent<ObserverChangedEvent> ObserverChanged { get; } =
        new("observer.changed", AppJsonContext.Default.ObserverChangedEvent);

    public static AppEvent<SkyChangedEvent> SkyChanged { get; } =
        new("sky.changed", AppJsonContext.Default.SkyChangedEvent);

    public static AppEvent<UiChangedEvent> UiChanged { get; } =
        new("ui.changed", AppJsonContext.Default.UiChangedEvent);

    public static AppEvent<NightChangedEvent> NightChanged { get; } =
        new("night.changed", AppJsonContext.Default.NightChangedEvent);

    public static AppEvent<SelectionChangedEvent> SelectionChanged { get; } =
        new("selection.changed", AppJsonContext.Default.SelectionChangedEvent);

    public static IReadOnlyList<IAppEvent> All { get; } = [ThemeChanged, ObserverChanged, SkyChanged, UiChanged, NightChanged, SelectionChanged];
}
