namespace AstroPlanner.Core.Interfaces;

/// <summary>
/// Artificial zenith sky brightness from the World Atlas 2015 (Falchi et al. 2016), spec §8.4.
/// Implemented in Data by an offline grid; Core only sees this lookup.
/// </summary>
public interface ILightPollutionAtlas
{
    /// <summary>
    /// Artificial zenith brightness at a site in mcd/m² (natural sky not included), or null when
    /// the site is outside the shipped grids or the grids are unavailable.
    /// </summary>
    double? ArtificialBrightness(double latitudeDegrees, double longitudeDegrees);
}
