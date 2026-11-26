namespace AstroPlanner.Core.Models;

/// <summary>
/// Where a floating window of the planetarium UI (v3) is and how it is arranged, restored at the
/// next start. <see cref="X"/>…<see cref="Height"/> are its floating rectangle in CSS pixels, kept
/// while it is maximised or docked so that restoring puts it back; <see cref="DockWidth"/> is
/// its width as a side sheet. A higher <see cref="ZOrder"/> is in front.
/// </summary>
public sealed record WindowLayout(
    string Id,
    bool IsOpen,
    bool IsMinimized,
    WindowArrangement Arrangement,
    int X,
    int Y,
    int Width,
    int Height,
    int DockWidth,
    int ZOrder);

/// <summary>How a window sits over the sky. New values are appended so that stored names stay valid.</summary>
public enum WindowArrangement
{
    Floating,
    Maximized,
    DockLeft,
    DockRight
}

/// <summary>Preferences that only the planetarium UI uses (Settings › Interface).</summary>
public sealed record InterfacePreferences
{
    /// <summary>Opaque panels instead of glass over the sky (spec §3.3).</summary>
    public bool ReduceTransparency { get; init; }

    /// <summary>The top bar and the dock fade out after a few seconds without input (spec §3.1).</summary>
    public bool AutoHideToolbars { get; init; } = true;

    /// <summary>A fast drag leaves the sky coasting to a stop (spec §8.1).</summary>
    public bool SkyInertia { get; init; } = true;

    /// <summary>Atmosphere quality of the sky: "low", "medium" or "high" (spec §8.2).</summary>
    public string SkyQuality { get; init; } = "medium";

    /// <summary>Colour of the light-pollution glow: "sodium", "led" or "neutral" (spec §8.2).</summary>
    public string LightPollutionTint { get; init; } = "led";

    /// <summary>Twinkling stars, at High quality only (spec §8.2; off by default).</summary>
    public bool Scintillation { get; init; }
}

/// <summary>The deep-sky survey layer of the planetarium UI (Settings › Survey, spec §8.3).</summary>
public sealed record SurveyPreferences
{
    public const long DefaultCacheLimitBytes = 1024L * 1024 * 1024;
    public const long MinimumCacheLimitBytes = 256L * 1024 * 1024;
    public const long MaximumCacheLimitBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>The survey shown when zooming in (a HiPS survey id); null shows none.</summary>
    public string? SurveyId { get; init; } = "DSSColor";

    /// <summary>"realistic": dimmed by the sky's brightness like the eye would see it; "survey": full contrast.</summary>
    public string Mode { get; init; } = "realistic";

    /// <summary>Disk space the survey tiles may take (least recently used tiles go first).</summary>
    public long CacheLimitBytes { get; init; } = DefaultCacheLimitBytes;
}
