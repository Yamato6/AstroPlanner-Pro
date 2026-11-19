using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;

namespace AstroPlanner.UI.ViewModels;

/// <summary>An object seen from the observer at the chosen time, with tonight's events.</summary>
public sealed class CelestialObjectObservation
{
    public required CelestialObject Target { get; init; }
    public required HorizontalCoordinates Coordinates { get; init; }
    public bool IsVisible { get; init; }

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

    /// <summary>Forecast cloud cover (%) at culmination; null without a forecast.</summary>
    public int? CloudCoverAtCulmination { get; init; }

    /// <summary>Angular distance to the Moon at the chosen time (degrees).</summary>
    public double? MoonSeparationDegrees { get; init; }

    /// <summary>Warning when the Moon makes observing harder; null when it does not.</summary>
    public string? MoonWarning { get; init; }

    public bool HasMoonWarning => MoonWarning is not null;
}
