using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>An object seen from the observer at the chosen time, with tonight's events.</summary>
public sealed class CelestialObjectObservation
{
    public required CelestialObject Target { get; init; }
    public required HorizontalCoordinates Coordinates { get; init; }
    public bool IsVisible { get; init; }

    /// <summary>IAU constellation at the chosen time (planets move between constellations).</summary>
    public string Constellation { get; init; } = string.Empty;

    /// <summary>Cross-identifications, e.g. "Also known as: NGC 1976, LBN 974"; null if none.</summary>
    public string? DesignationsText => Target.Designations.Count == 0
        ? null
        : DisplayNames.Format(Strings.DetailDesignationsFormat, string.Join(", ", Target.Designations.Take(8)));

    /// <summary>"Size: 90′ × 60′"; null if unknown.</summary>
    public string? SizeText => DisplayNames.Size(Target.MajorAxisArcmin, Target.MinorAxisArcmin) is { Length: > 0 } size
        ? DisplayNames.Format(Strings.DetailSizeFormat, size)
        : null;

    /// <summary>"Surface brightness: 23.6 mag/arcsec²"; null if unknown.</summary>
    public string? SurfaceBrightnessText => Target.SurfaceBrightness is { } brightness
        ? DisplayNames.Format(Strings.DetailSurfaceBrightnessFormat, brightness)
        : null;

    /// <summary>Best window in darkness tonight and the time above the minimum altitude.</summary>
    public string BestWindowText { get; init; } = string.Empty;

    public string VisibilityStatus => Coordinates.AltitudeDegrees < 0
        ? Strings.StatusNotVisible
        : IsVisible ? Strings.StatusVisible : Strings.StatusBelowThreshold;

    public DateTime? RiseTimeLocal { get; init; }
    public DateTime? VisibilityStartTimeLocal { get; init; }
    public DateTime? BestObservationTimeLocal { get; init; }
    public DateTime? SetTimeLocal { get; init; }
    public double MaximumAltitudeDegrees { get; init; }

    /// <summary>Apparent magnitude at the chosen time (for planets it changes with the date).</summary>
    public double Magnitude { get; init; }

    /// <summary>Distance, phase and apparent size if the object is a planet; null otherwise.</summary>
    public PlanetPosition? Planet { get; init; }

    public bool IsPlanet => Planet is not null;

    /// <summary>
    /// For comets and asteroids: "r 1.23 AU · Δ 0.85 AU · elongation 75°" and the elements' source
    /// and epoch; null for other objects.
    /// </summary>
    public string? SmallBodyText { get; init; }

    /// <summary>Stars: spectral type, colour, distance and variability; null for other objects.</summary>
    public string? StarText { get; init; }

    /// <summary>Double stars: one line per pair (closest first); null for single stars and other objects.</summary>
    public string? DoubleStarText { get; init; }

    /// <summary>Forecast cloud cover (%) at culmination; null without a forecast.</summary>
    public int? CloudCoverAtCulmination { get; init; }

    /// <summary>Angular distance to the Moon at the chosen time (degrees).</summary>
    public double? MoonSeparationDegrees { get; init; }

    /// <summary>Warning when the Moon makes observing harder; null when it does not.</summary>
    public string? MoonWarning { get; init; }

    public bool HasMoonWarning => MoonWarning is not null;
}
