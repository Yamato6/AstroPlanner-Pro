namespace AstroPlanner.SkyBridge;

// Payloads of the sky protocol (spec §5.4). Property names are serialized in camelCase and must
// match src/web/packages/sky-engine/src/bridge/schema.ts; the contract tests (tests/fixtures/bridge) keep
// both sides in step. Angles are in degrees, times are ISO 8601 UTC strings.

/// <summary>Message type names.</summary>
public static class SkyMessageTypes
{
    // C# → renderer
    public const string Init = "init";
    public const string SetObserver = "setObserver";
    public const string SetTime = "setTime";
    public const string SetTimeRate = "setTimeRate";
    public const string FrameTransform = "frameTransform";
    public const string MovingBodies = "movingBodies";
    public const string SetLayers = "setLayers";
    public const string SetHighlights = "setHighlights";
    public const string CenterOn = "centerOn";
    public const string SetViewMode = "setViewMode";
    public const string SetTheme = "setTheme";
    public const string SetFov = "setFov";
    public const string SetSkyAppearance = "setSkyAppearance";

    // Both directions
    public const string SelectObject = "selectObject";

    // Renderer → C#
    public const string Ready = "ready";
    public const string FirstFrame = "firstFrame";
    public const string Hover = "hover";
    public const string ViewChanged = "viewChanged";
    public const string Stats = "stats";
    public const string Shortcut = "shortcut";
    public const string FovChanged = "fovChanged";
    public const string Error = "error";
}

/// <summary>Keyboard shortcuts pressed while the renderer has the focus (spec §10).</summary>
public static class SkyShortcuts
{
    public const string TogglePause = "togglePause";   // Space
    public const string ViewDome = "viewDome";         // 1
    public const string ViewAllSky = "viewAllSky";     // 2
    public const string ToggleFov = "toggleFov";       // F
    public const string FocusSearch = "focusSearch";   // /
}

/// <summary>Kinds of objects the renderer can show and pick.</summary>
public static class SkyObjectKinds
{
    public const string Star = "star";
    public const string DeepSky = "dso";
    public const string Body = "body";
}

/// <summary>View modes of the renderer.</summary>
public static class SkyViewModes
{
    public const string Dome = "dome";
    public const string AllSky = "allSky";
    public const string Finder = "finder";
}

/// <summary>
/// Saemundsson refraction R' = A / tan(h + B / (h + C)) + D arcminutes, applied for true altitudes
/// above <paramref name="MinimumAltitudeDegrees"/> (the Core <c>Refraction</c> model).
/// </summary>
public sealed record RefractionModel(double A, double B, double C, double D, double MinimumAltitudeDegrees)
{
    public static RefractionModel Standard { get; } = new(1.02, 10.3, 5.11, 0.0019279, -2.0);

    /// <summary>
    /// The model at a site: both terms scaled by the pressure of its elevation
    /// (Core <c>Refraction.PressureScale</c>), exactly as <c>Refraction.ApparentAltitude</c>.
    /// </summary>
    public static RefractionModel ForElevation(double elevationMeters)
    {
        var scale = AstroPlanner.Core.Algorithms.Refraction.PressureScale(elevationMeters);
        return Standard with { A = Standard.A * scale, D = Standard.D * scale };
    }
}

/// <summary>Visible layers and limits.</summary>
public sealed record SkyLayers(
    bool Stars = true,
    bool ConstellationLines = true,
    bool ConstellationBoundaries = false,
    bool ConstellationNames = true,
    bool AltAzGrid = false,
    bool EquatorialGrid = false,
    bool Ecliptic = false,
    bool Meridian = false,
    bool MilkyWay = true,
    bool DeepSky = true,
    bool Bodies = true,
    bool Labels = true,
    bool Ground = true,
    bool SkyBrightness = true,
    bool Satellites = true,
    double StarLimitMagnitude = 8.5,
    double DeepSkyLimitMagnitude = 12.0);

/// <summary>First message after <c>ready</c>: data location, refraction model and initial state.</summary>
public sealed record InitPayload(
    string DataBaseUrl,
    RefractionModel Refraction,
    SkyLayers Layers,
    string ViewMode,
    string Theme,
    bool ShowStats);

/// <summary>A horizon mask point.</summary>
public sealed record HorizonPointDto(double Azimuth, double Altitude);

/// <summary>Observer site.</summary>
public sealed record ObserverPayload(
    string Name,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double ElevationMeters,
    double MinimumAltitudeDegrees,
    IReadOnlyList<HorizonPointDto> Horizon,
    RefractionModel Refraction);

/// <summary>Sky time and the rate it advances at (1 = real time, 0 = paused).</summary>
public sealed record TimePayload(string Utc, double Rate);

/// <summary>Rate only (time keeps flowing from the current renderer time).</summary>
public sealed record TimeRatePayload(double Rate);

/// <summary>
/// J2000 → horizontal transform (Core <c>SkyFrame</c>): H(t) = L(φ)·Rz(−LST(t))·P with
/// LST(t) = lstDegrees + lstRateDegreesPerSecond·(t − epochUtc). <c>precession</c> is row-major.
/// </summary>
public sealed record FrameTransformPayload(
    string EpochUtc,
    IReadOnlyList<double> Precession,
    double LatitudeDegrees,
    double LstDegrees,
    double LstRateDegreesPerSecond);

/// <summary>A sampled body track: J2000-frame unit vectors (x, y, z flattened) every <c>stepSeconds</c>.</summary>
public sealed record TrackDto(string StartUtc, double StepSeconds, IReadOnlyList<double> Vectors);

/// <summary>
/// A Sun/Moon/planet/comet/asteroid/satellite with its track. <c>Sunlit</c> (satellites only, else
/// null) says for each track sample whether the Sun lights the satellite (1) or it is in the Earth's shadow (0).
/// </summary>
public sealed record MovingBodyDto(
    string Id,
    string Kind,
    string Name,
    double Magnitude,
    double DiameterArcsec,
    double PhaseAngleDegrees,
    double IlluminatedFraction,
    double BrightLimbAngleDegrees,
    TrackDto Track,
    IReadOnlyList<int>? Sunlit = null);

public sealed record MovingBodiesPayload(IReadOnlyList<MovingBodyDto> Bodies);

/// <summary>Objects drawn emphasized (session plan) and the current filter result.</summary>
public sealed record HighlightsPayload(IReadOnlyList<string> Plan);

/// <summary>Selection; a null id clears it. Sent both ways.</summary>
public sealed record SelectionPayload(string? Kind, string? Id);

/// <summary>Turns the view to a J2000 direction, optionally changing the field of view.</summary>
public sealed record CenterOnPayload(double X, double Y, double Z, double? FovDegrees);

public sealed record ViewModePayload(string Mode);

public sealed record ThemePayload(string Theme);

/// <summary>A J2000 direction (unit vector).</summary>
public sealed record VectorDto(double X, double Y, double Z);

/// <summary>
/// Camera field-of-view overlay: the sensor rectangle projected on the sky. <c>rotationDegrees</c>
/// is the position angle of the image's "up" side, from celestial north through east (0 = north
/// up). A null center puts it at the middle of the current view (the renderer then reports it
/// with <c>fovChanged</c>).
/// </summary>
public sealed record FovPayload(
    bool Visible,
    double WidthDegrees,
    double HeightDegrees,
    double RotationDegrees,
    VectorDto? Center,
    string Label);

/// <summary>
/// Sampled sky brightness (Core <c>SkyAppearanceCalculator</c>, the feasibility model at the
/// zenith), interpolated by the renderer like the body tracks every <c>stepSeconds</c> from
/// <c>startUtc</c>: the total and naked-eye limiting magnitude, and the twilight and moonlight
/// parts (mag/arcsec², <see cref="SkyAppearancePayload.None"/> = none). The renderer spreads the
/// site's glow (<c>siteSqm</c>, brightened by <c>brighteningPerAirmass</c>), the moonlight and the
/// twilight over the sky with the laws of <c>SkyBackground</c>, and dims stars by
/// <c>extinction</c> (mag/air mass).
/// </summary>
public sealed record SkyAppearancePayload(
    string StartUtc,
    double StepSeconds,
    IReadOnlyList<double> ZenithSqm,
    IReadOnlyList<double> LimitingMagnitude,
    IReadOnlyList<double> TwilightSqm,
    IReadOnlyList<double> MoonlightSqm,
    double SiteSqm,
    double Extinction,
    double BrighteningPerAirmass)
{
    /// <summary>Brightness of a part that is absent (JSON has no infinity).</summary>
    public const double None = 99;
}

/// <summary>Renderer started: versions and capabilities.</summary>
public sealed record ReadyPayload(string RendererVersion, int WebGlVersion);

/// <summary>Time from navigation start to the first rendered frame.</summary>
public sealed record FirstFramePayload(double Milliseconds);

/// <summary>Object under the pointer (null id = nothing).</summary>
public sealed record HoverPayload(string? Kind, string? Id, string? Label);

/// <summary>Camera of the dome view, so that it can be restored (sent in the dome mode only).</summary>
public sealed record ViewChangedPayload(string Mode, double AzimuthDegrees, double AltitudeDegrees, double FovDegrees);

/// <summary>Frame statistics (periodic, or the result of a benchmark run).</summary>
public sealed record StatsPayload(double Fps, double P95FrameMs, int Frames, int Width, int Height, int Stars, bool Benchmark);

public sealed record ShortcutPayload(string Action);

/// <summary>The user dragged or rotated the field-of-view overlay: its new center (J2000) and rotation.</summary>
public sealed record FovChangedPayload(double X, double Y, double Z, double RotationDegrees);

public sealed record ErrorPayload(string Message);
