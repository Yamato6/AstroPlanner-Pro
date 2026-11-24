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
[JsonSerializable(typeof(ThemeChangedEvent))]
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

    public static IReadOnlyList<IRpcMethod> All { get; } = [AppInfo, ObserverGet];
}

/// <summary>Every event of the app channel.</summary>
public static class AppEvents
{
    public static AppEvent<ThemeChangedEvent> ThemeChanged { get; } =
        new("theme.changed", AppJsonContext.Default.ThemeChangedEvent);

    public static AppEvent<ObserverChangedEvent> ObserverChanged { get; } =
        new("observer.changed", AppJsonContext.Default.ObserverChangedEvent);

    public static IReadOnlyList<IAppEvent> All { get; } = [ThemeChanged, ObserverChanged];
}
