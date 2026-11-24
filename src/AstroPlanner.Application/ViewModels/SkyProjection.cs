namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// Polar projection of the sky for the chart: the zenith at the center and the horizon on
/// a circle of radius 90 (distance to the center = 90° - altitude). North up and east
/// left, as the sky looks when looking up (like a planisphere).
/// </summary>
public static class SkyProjection
{
    public const double HorizonRadius = 90;

    public static (double X, double Y) ToXY(double altitudeDegrees, double azimuthDegrees)
    {
        var radius = HorizonRadius - altitudeDegrees;
        var azimuth = azimuthDegrees * Math.PI / 180.0;
        return (-radius * Math.Sin(azimuth), radius * Math.Cos(azimuth));
    }
}

/// <summary>An object drawn on the sky chart.</summary>
public sealed record SkyMapPoint(
    string Id,
    double AltitudeDegrees,
    double AzimuthDegrees,
    double Magnitude,
    bool IsVisible,
    bool IsInPlan,
    bool IsSelected,
    bool IsPlanet = false,
    bool HasLabel = true)
{
    public (double X, double Y) Position => SkyProjection.ToXY(AltitudeDegrees, AzimuthDegrees);
}
