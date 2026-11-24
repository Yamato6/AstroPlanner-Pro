using System.ComponentModel.DataAnnotations;

namespace AstroPlanner.SkyBridge.App;

// Contracts of the app channel (bridge v2, docs/bridge.md): requests, responses and event payloads
// exchanged with the web UI. C# is the source of truth: tools/AstroPlanner.BridgeGen turns these
// records into zod schemas and TypeScript types (src/web/packages/bridge-client/src/generated), and
// a test fails while that file is stale. Constraints are DataAnnotations attributes on the
// properties: StringLength, Range, Length (lists) and AllowedValues; both sides enforce them.
// Validation recurses into nested contract records and lists of them (ContractValidator).
// Property names are camelCase on the wire.

/// <summary>Theme names shared with the sky protocol (<c>setTheme</c>).</summary>
public static class AppThemes
{
    public const string Dark = "dark";
    public const string Night = "night";
}

/// <summary><c>app.info</c> takes no parameters.</summary>
public sealed record AppInfoRequest;

/// <summary>What the web UI needs at start-up: versions and the current theme.</summary>
public sealed record AppInfoResponse(
    [property: StringLength(32, MinimumLength = 1)] string Version,
    [property: AllowedValues(AppThemes.Dark, AppThemes.Night)] string Theme,
    [property: Range(1, 1000)] int SkyProtocolVersion,
    [property: Range(1, 1000)] int AppProtocolVersion);

/// <summary>How the zenith sky brightness was obtained (<c>SkyQualitySource</c> in Core).</summary>
public static class SkyQualitySources
{
    public const string Manual = "manual";
    public const string Atlas = "atlas";
    public const string Default = "default";
}

/// <summary><c>observer.get</c> takes no parameters.</summary>
public sealed record ObserverGetRequest;

/// <summary>
/// The site, time and sky that everything in the app is computed for (<c>observer.get</c>). Local
/// times are the site's wall clock in ISO 8601 without an offset (<c>2026-01-15T22:00:00</c>).
/// <see cref="Revision"/> grows with every change that <c>observer.changed</c> announces.
/// </summary>
public sealed record ObserverSnapshot(
    [property: Range(0, int.MaxValue)] int Revision,
    [property: StringLength(200)] string LocationName,
    [property: Range(-90.0, 90.0)] double LatitudeDegrees,
    [property: Range(-180.0, 180.0)] double LongitudeDegrees,
    [property: Range(-1000.0, 10000.0)] double ElevationMeters,
    [property: StringLength(100, MinimumLength = 1)] string TimeZoneId,
    [property: StringLength(19, MinimumLength = 19)] string ObservationTimeLocal,
    [property: Range(-90.0, 90.0)] double MinimumAltitudeDegrees,
    [property: Range(0.0, 30.0)] double ZenithSqm,
    [property: AllowedValues(SkyQualitySources.Manual, SkyQualitySources.Atlas, SkyQualitySources.Default)] string ZenithSqmSource,
    ObserverNight? Night,
    ObserverMoon? Moon,
    [property: StringLength(500)] string? InputError);

/// <summary>The observing night: its window and, when the Sun sets and gets low enough, the twilights.</summary>
public sealed record ObserverNight(
    [property: StringLength(19, MinimumLength = 19)] string WindowStartLocal,
    [property: StringLength(19, MinimumLength = 19)] string WindowEndLocal,
    [property: StringLength(19, MinimumLength = 19)] string? SunsetLocal,
    [property: StringLength(19, MinimumLength = 19)] string? SunriseLocal,
    [property: StringLength(19, MinimumLength = 19)] string? AstronomicalDuskLocal,
    [property: StringLength(19, MinimumLength = 19)] string? AstronomicalDawnLocal);

/// <summary>The Moon at the observation time.</summary>
public sealed record ObserverMoon(
    [property: Range(0.0, 100.0)] double IlluminatedPercent,
    [property: Range(-90.0, 90.0)] double AltitudeDegrees,
    bool IsWaxing);

/// <summary><c>observer.changed</c>: the site, time or sky changed; fetch <c>observer.get</c> when <see cref="Revision"/> is newer.</summary>
public sealed record ObserverChangedEvent(
    [property: Range(0, int.MaxValue)] int Revision);

/// <summary><c>theme.changed</c>: the user switched between the dark and the night-vision theme.</summary>
public sealed record ThemeChangedEvent(
    [property: AllowedValues(AppThemes.Dark, AppThemes.Night)] string Theme);

/// <summary>Why a request failed (the <c>error</c> member of a response).</summary>
public sealed record RpcErrorPayload(
    [property: AllowedValues(RpcErrorCodes.BadRequest, RpcErrorCodes.UnknownMethod, RpcErrorCodes.Timeout,
        RpcErrorCodes.Cancelled, RpcErrorCodes.TooLarge, RpcErrorCodes.Busy, RpcErrorCodes.Internal)] string Code,
    [property: StringLength(1024, MinimumLength = 1)] string Message);

/// <summary>Error codes of a failed request.</summary>
public static class RpcErrorCodes
{
    /// <summary>The parameters do not match the method's contract.</summary>
    public const string BadRequest = "bad-request";

    /// <summary>No such method.</summary>
    public const string UnknownMethod = "unknown-method";

    /// <summary>The host did not answer in time (<see cref="RpcDispatcher.DefaultTimeout"/>).</summary>
    public const string Timeout = "timeout";

    /// <summary>The web UI cancelled the request.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The answer would exceed <see cref="AppMessageCodec.MaximumResponseLength"/>: page it.</summary>
    public const string TooLarge = "too-large";

    /// <summary>Too many requests are running (<see cref="RpcDispatcher.MaximumConcurrentRequests"/>): retry later.</summary>
    public const string Busy = "busy";

    /// <summary>The handler failed; details are in the host log only.</summary>
    public const string Internal = "internal";
}
