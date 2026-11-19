using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Simple rule for when moonlight gets in the way of observing an object: the Moon is
/// above the horizon, reasonably lit and close to the object.
/// </summary>
public static class MoonInterference
{
    /// <summary>Below this angular distance (degrees) the Moon interferes.</summary>
    public const double SeparationDegrees = 30;

    /// <summary>Minimum illuminated fraction (25 %) for moonlight to matter.</summary>
    public const double MinimumIllumination = 0.25;

    /// <summary>
    /// True if the Moon is above the horizon, at least <see cref="MinimumIllumination"/> lit
    /// and less than <see cref="SeparationDegrees"/> away from the object.
    /// </summary>
    public static bool Interferes(MoonState moon, double separationDegrees)
    {
        ArgumentNullException.ThrowIfNull(moon);
        return moon.IsAboveHorizon
            && moon.IlluminatedFraction >= MinimumIllumination
            && separationDegrees < SeparationDegrees;
    }
}
