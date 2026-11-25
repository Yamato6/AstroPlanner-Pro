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
/// times are the site's wall clock in ISO 8601 without an offset (<c>2026-01-15T22:00:00</c>);
/// <see cref="TimeZoneId"/> is an IANA id (<c>America/Mexico_City</c>).
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

/// <summary>A method that takes no parameters.</summary>
public sealed record NoParameters;

/// <summary>Keys of the sky's layer switches (the renderer's <c>setLayers</c> members).</summary>
public static class SkyLayerKeys
{
    public const string Stars = "stars";
    public const string ConstellationLines = "constellationLines";
    public const string ConstellationNames = "constellationNames";
    public const string ConstellationBoundaries = "constellationBoundaries";
    public const string MilkyWay = "milkyWay";
    public const string DeepSky = "deepSky";
    public const string Bodies = "bodies";
    public const string Satellites = "satellites";
    public const string Labels = "labels";
    public const string AltAzGrid = "altAzGrid";
    public const string EquatorialGrid = "equatorialGrid";
    public const string Ecliptic = "ecliptic";
    public const string Meridian = "meridian";
    public const string Ground = "ground";
    public const string SkyBrightness = "skyBrightness";
}

/// <summary>
/// The sky's time, layers, view mode and selection (<c>sky.get</c>, and the answer of every
/// <c>sky.*</c> command). The clock runs on the page: the time now is
/// <c>ClockUtcMilliseconds + Rate × (elapsed real time)</c>, so playing needs no messages.
/// </summary>
public sealed record SkySnapshot(
    [property: Range(0, int.MaxValue)] int Revision,
    [property: Range(-62135596800000.0, 253402300799000.0)] long ClockUtcMilliseconds,
    [property: Range(0, 3600)] int Rate,
    bool IsPlaying,
    [property: Length(1, 10)] IReadOnlyList<int> Rates,
    [property: AllowedValues(SkyViewModes.Dome, SkyViewModes.AllSky, SkyViewModes.Finder)] string ViewMode,
    [property: Length(0, 32)] IReadOnlyList<SkyLayerState> Layers,
    [property: Range(-2.0, 20.0)] double StarLimitMagnitude,
    SkySelection? Selection);

/// <summary>One layer switch: its key, its display name and whether it is on.</summary>
public sealed record SkyLayerState(
    [property: AllowedValues(SkyLayerKeys.Stars, SkyLayerKeys.ConstellationLines, SkyLayerKeys.ConstellationNames,
        SkyLayerKeys.ConstellationBoundaries, SkyLayerKeys.MilkyWay, SkyLayerKeys.DeepSky, SkyLayerKeys.Bodies,
        SkyLayerKeys.Satellites, SkyLayerKeys.Labels, SkyLayerKeys.AltAzGrid, SkyLayerKeys.EquatorialGrid,
        SkyLayerKeys.Ecliptic, SkyLayerKeys.Meridian, SkyLayerKeys.Ground, SkyLayerKeys.SkyBrightness)] string Key,
    [property: StringLength(80, MinimumLength = 1)] string Name,
    bool Enabled);

/// <summary>What is selected: a catalog object (with its id) or something only the sky knows (Sun, Moon, a star).</summary>
public sealed record SkySelection(
    [property: StringLength(64, MinimumLength = 1)] string? Id,
    [property: StringLength(200, MinimumLength = 1)] string Name,
    [property: StringLength(80, MinimumLength = 1)] string? TypeName);

/// <summary><c>sky.step</c>: moves the time by this many minutes (and pauses).</summary>
public sealed record SkyStepRequest(
    [property: Range(-525600, 525600)] int Minutes);

/// <summary><c>sky.setRate</c>: the speed of time while playing (the nearest supported rate is used).</summary>
public sealed record SkySetRateRequest(
    [property: Range(1, 3600)] int Rate);

/// <summary><c>sky.setLayer</c>: turns one layer on or off.</summary>
public sealed record SkySetLayerRequest(
    [property: AllowedValues(SkyLayerKeys.Stars, SkyLayerKeys.ConstellationLines, SkyLayerKeys.ConstellationNames,
        SkyLayerKeys.ConstellationBoundaries, SkyLayerKeys.MilkyWay, SkyLayerKeys.DeepSky, SkyLayerKeys.Bodies,
        SkyLayerKeys.Satellites, SkyLayerKeys.Labels, SkyLayerKeys.AltAzGrid, SkyLayerKeys.EquatorialGrid,
        SkyLayerKeys.Ecliptic, SkyLayerKeys.Meridian, SkyLayerKeys.Ground, SkyLayerKeys.SkyBrightness)] string Key,
    bool Enabled);

/// <summary><c>sky.setViewMode</c>: dome, all-sky or finder chart.</summary>
public sealed record SkySetViewModeRequest(
    [property: AllowedValues(SkyViewModes.Dome, SkyViewModes.AllSky, SkyViewModes.Finder)] string Mode);

/// <summary><c>sky.changed</c>: time, layers, view mode or selection changed.</summary>
public sealed record SkyChangedEvent(
    [property: Range(0, int.MaxValue)] int Revision);

/// <summary><c>theme.changed</c>: the user switched between the dark and the night-vision theme.</summary>
public sealed record ThemeChangedEvent(
    [property: AllowedValues(AppThemes.Dark, AppThemes.Night)] string Theme);

/// <summary>
/// The interface state the host keeps (<c>ui.get</c>, and the answer of every <c>ui.*</c>
/// command): theme, reduce transparency and hiding idle toolbars (saved), full screen (the window's).
/// </summary>
public sealed record UiSnapshot(
    [property: Range(0, int.MaxValue)] int Revision,
    [property: AllowedValues(AppThemes.Dark, AppThemes.Night)] string Theme,
    bool ReduceTransparency,
    bool AutoHideToolbars,
    bool IsFullScreen);

/// <summary><c>sky.setTime</c>: shows the sky at this instant (and pauses), e.g. from the darkness bar.</summary>
public sealed record SkySetTimeRequest(
    [property: Range(-62135596800000.0, 253402300799000.0)] long UtcMilliseconds);

/// <summary>
/// The observing night sampled every <see cref="StepMinutes"/> (on whole steps of the site's clock)
/// from an hour before sunset to an hour after sunrise (<c>night.get</c>), for the darkness bar: Sun and Moon altitudes, the Moon's
/// illumination, and cloud cover when there is a forecast (empty otherwise). Sample k is at
/// <c>StartUtcMilliseconds + k × StepMinutes</c>.
/// </summary>
public sealed record NightTimeline(
    [property: Range(0, int.MaxValue)] int Revision,
    [property: Range(-62135596800000.0, 253402300799000.0)] long StartUtcMilliseconds,
    [property: Range(1, 60)] int StepMinutes,
    [property: Length(0, 400)] IReadOnlyList<double> SunAltitudeDegrees,
    [property: Length(0, 400)] IReadOnlyList<double> MoonAltitudeDegrees,
    [property: Range(0.0, 100.0)] double MoonIlluminatedPercent,
    [property: Length(0, 400)] IReadOnlyList<double> CloudPercent);

/// <summary><c>night.changed</c>: the night, the site or the forecast changed.</summary>
public sealed record NightChangedEvent(
    [property: Range(0, int.MaxValue)] int Revision);

/// <summary>Verdicts of the feasibility model (<c>FeasibilityVerdict</c> in Core).</summary>
public static class FeasibilityVerdicts
{
    public const string NotPossible = "notPossible";
    public const string Unlikely = "unlikely";
    public const string Marginal = "marginal";
    public const string Good = "good";
    public const string Excellent = "excellent";
}

/// <summary>
/// The selected object for the object card (<c>selection.get</c>, and the answer of every
/// <c>selection.*</c> command); <see cref="Target"/> is null when nothing is selected.
/// </summary>
public sealed record SelectionSnapshot(
    [property: Range(0, int.MaxValue)] int Revision,
    SelectedObject? Target);

/// <summary>
/// What the card shows, formatted by C#. A catalog object has an <see cref="Id"/> and its data;
/// something only the sky knows (the Sun, the Moon, a star off the catalog) has a title only.
/// Altitude and azimuth are at the observation time; the transit is the site's wall clock.
/// </summary>
public sealed record SelectedObject(
    [property: StringLength(64, MinimumLength = 1)] string? Id,
    [property: StringLength(200, MinimumLength = 1)] string Title,
    [property: StringLength(80, MinimumLength = 1)] string? TypeName,
    [property: StringLength(80, MinimumLength = 1)] string? Constellation,
    [property: StringLength(16, MinimumLength = 1)] string? Magnitude,
    [property: StringLength(80, MinimumLength = 1)] string? Coordinates,
    [property: Range(-90.0, 90.0)] double? AltitudeDegrees,
    [property: Range(0.0, 360.0)] double? AzimuthDegrees,
    [property: StringLength(19, MinimumLength = 19)] string? TransitLocal,
    [property: Range(-90.0, 90.0)] double? MaximumAltitudeDegrees,
    [property: StringLength(80, MinimumLength = 1)] string? BestWindow,
    [property: AllowedValues(FeasibilityVerdicts.NotPossible, FeasibilityVerdicts.Unlikely, FeasibilityVerdicts.Marginal,
        FeasibilityVerdicts.Good, FeasibilityVerdicts.Excellent)] string? Verdict,
    [property: StringLength(40, MinimumLength = 1)] string? VerdictText,
    [property: Range(0, 100)] int? Score,
    [property: StringLength(300, MinimumLength = 1)] string? Recommended,
    [property: Length(0, 8)] IReadOnlyList<string> Reasons,
    bool IsInPlan);

/// <summary><c>selection.changed</c>: another object was selected, or the selected one changed (plan, feasibility).</summary>
public sealed record SelectionChangedEvent(
    [property: Range(0, int.MaxValue)] int Revision);

/// <summary><c>search.query</c>: catalog objects whose ids or names match the text, best first.</summary>
public sealed record SearchQueryRequest(
    [property: StringLength(100, MinimumLength = 1)] string Text,
    [property: Range(1, 50)] int Limit);

/// <summary>One match of <c>search.query</c>.</summary>
public sealed record SearchResult(
    [property: StringLength(64, MinimumLength = 1)] string Id,
    [property: StringLength(200, MinimumLength = 1)] string Title,
    [property: StringLength(80, MinimumLength = 1)] string? TypeName,
    [property: StringLength(16, MinimumLength = 1)] string? Magnitude,
    [property: StringLength(80, MinimumLength = 1)] string? Constellation);

public sealed record SearchResults(
    [property: Length(0, 50)] IReadOnlyList<SearchResult> Results);

/// <summary><c>search.go</c>: selects a catalog object and turns the view to it.</summary>
public sealed record SearchGoRequest(
    [property: StringLength(64, MinimumLength = 1)] string Id);

/// <summary><c>search.resolve</c>: a name the catalog lacks, resolved online (Sesame) when allowed; the view turns to it.</summary>
public sealed record SearchResolveRequest(
    [property: StringLength(100, MinimumLength = 1)] string Text);

/// <summary>Whether it was found, and the message the classic search shows.</summary>
public sealed record SearchResolveResponse(
    bool Found,
    [property: StringLength(500, MinimumLength = 1)] string Message);

/// <summary><c>ui.setTheme</c>: dark or night vision (saved, like the classic window's switch).</summary>
public sealed record UiSetThemeRequest(
    [property: AllowedValues(AppThemes.Dark, AppThemes.Night)] string Theme);

/// <summary>Turns an option on or off (<c>ui.setReduceTransparency</c>, <c>ui.setAutoHide</c>, <c>ui.setFullScreen</c>).</summary>
public sealed record SwitchRequest(bool Enabled);

/// <summary><c>ui.changed</c>: theme, transparency or full screen changed.</summary>
public sealed record UiChangedEvent(
    [property: Range(0, int.MaxValue)] int Revision);

/// <summary>Ids of the planetarium UI's windows (spec §3.4).</summary>
public static class WindowIds
{
    public const string Tonight = "tonight";
    public const string Targets = "targets";
    public const string Plan = "plan";
    public const string Logbook = "logbook";
    public const string Equipment = "equipment";
    public const string Sites = "sites";
    public const string MilkyWay = "milkyWay";
    public const string Settings = "settings";
    public const string Help = "help";
}

/// <summary>How a window sits over the sky (<c>WindowArrangement</c> in Core).</summary>
public static class WindowArrangements
{
    public const string Floating = "floating";
    public const string Maximized = "maximized";
    public const string DockLeft = "dockLeft";
    public const string DockRight = "dockRight";
}

/// <summary>
/// One window of the layout: open or not, minimised to the rail, its arrangement, its floating
/// rectangle (CSS pixels, kept while maximised or docked), its width as a side sheet and its
/// stacking order (higher is in front).
/// </summary>
public sealed record WindowLayoutState(
    [property: AllowedValues(WindowIds.Tonight, WindowIds.Targets, WindowIds.Plan, WindowIds.Logbook, WindowIds.Equipment,
        WindowIds.Sites, WindowIds.MilkyWay, WindowIds.Settings, WindowIds.Help)] string Id,
    bool IsOpen,
    bool IsMinimized,
    [property: AllowedValues(WindowArrangements.Floating, WindowArrangements.Maximized, WindowArrangements.DockLeft,
        WindowArrangements.DockRight)] string Arrangement,
    [property: Range(-20000, 20000)] int X,
    [property: Range(-20000, 20000)] int Y,
    [property: Range(1, 20000)] int Width,
    [property: Range(1, 20000)] int Height,
    [property: Range(1, 20000)] int DockWidth,
    [property: Range(0, 10000)] int ZOrder);

/// <summary>The saved window layout (<c>layout.load</c>; <c>layout.save</c> replaces it).</summary>
public sealed record LayoutSnapshot(
    [property: Length(0, 32)] IReadOnlyList<WindowLayoutState> Windows);

/// <summary>One credit line of About: what (data, algorithms…), who and the license; a link when the source asks for one.</summary>
public sealed record AboutCredit(
    [property: StringLength(100, MinimumLength = 1)] string Title,
    [property: StringLength(2000, MinimumLength = 1)] string Text,
    [property: StringLength(200, MinimumLength = 1)] string? Url);

/// <summary><c>app.about</c>: the About window's content (domain text, formatted by C#).</summary>
public sealed record AboutResponse(
    [property: StringLength(32, MinimumLength = 1)] string Version,
    [property: StringLength(2000, MinimumLength = 1)] string Description,
    [property: Length(0, 16)] IReadOnlyList<AboutCredit> Credits,
    [property: StringLength(1000, MinimumLength = 1)] string YourData,
    [property: StringLength(1024, MinimumLength = 1)] string DataFolder,
    [property: StringLength(500, MinimumLength = 1)] string License,
    [property: StringLength(200, MinimumLength = 1)] string RepositoryUrl);

/// <summary><c>app.openLink</c>: opens an https link in the browser, only to the sites the app links to (SafeLinks).</summary>
public sealed record OpenLinkRequest(
    [property: StringLength(2048, MinimumLength = 1)] string Url);

/// <summary>Whether the link was opened (false: not on the allow-list).</summary>
public sealed record OpenLinkResponse(bool Opened);

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
