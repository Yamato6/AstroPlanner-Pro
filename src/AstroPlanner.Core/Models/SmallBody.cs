namespace AstroPlanner.Core.Models;

/// <summary>Kind of minor body.</summary>
public enum SmallBodyKind
{
    Comet,
    Asteroid
}

/// <summary>
/// Heliocentric osculating orbit in perihelion form, J2000.0 ecliptic and equinox (as the Minor
/// Planet Center publishes them). Valid for any eccentricity: elliptic, parabolic (e = 1) and
/// hyperbolic.
/// </summary>
/// <param name="PerihelionJulianDate">Time of perihelion passage, JD (TT; used as UT, see the remarks).</param>
/// <param name="EpochJulianDate">Osculation epoch, JD; the elements are best near it.</param>
/// <remarks>The ~70 s between TT and UT moves a comet by far less than the model's error.</remarks>
public sealed record OrbitalElements(
    double PerihelionDistanceAu,
    double Eccentricity,
    double PerihelionJulianDate,
    double ArgumentOfPerihelionDegrees,
    double AscendingNodeDegrees,
    double InclinationDegrees,
    double EpochJulianDate)
{
    /// <summary>Gaussian gravitational constant (AU^1.5 / day).</summary>
    public const double GaussianConstant = 0.01720209895;

    /// <summary>
    /// From mean-anomaly elements (MPCORB): semi-major axis, eccentricity, the angles and the mean
    /// anomaly at the epoch. Only for elliptic orbits.
    /// </summary>
    public static OrbitalElements FromMeanAnomaly(
        double semiMajorAxisAu, double eccentricity, double inclinationDegrees, double ascendingNodeDegrees,
        double argumentOfPerihelionDegrees, double meanAnomalyDegrees, double epochJulianDate)
    {
        if (semiMajorAxisAu <= 0 || eccentricity is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(eccentricity), "Mean-anomaly elements must describe an elliptic orbit.");
        }

        var meanMotionDegreesPerDay = GaussianConstant / Math.Pow(semiMajorAxisAu, 1.5) * 180 / Math.PI;
        var m = meanAnomalyDegrees % 360;
        if (m > 180)
        {
            m -= 360;
        }

        return new OrbitalElements(
            semiMajorAxisAu * (1 - eccentricity), eccentricity, epochJulianDate - (m / meanMotionDegreesPerDay),
            argumentOfPerihelionDegrees, ascendingNodeDegrees, inclinationDegrees, epochJulianDate);
    }
}

/// <summary>A comet or asteroid of the catalog: its orbit and how its brightness is modelled.</summary>
/// <param name="AbsoluteMagnitude">H (asteroids) or the comet's total absolute magnitude.</param>
/// <param name="Slope">G for asteroids (H-G system); for comets the MPC "slope" K/2.5 (m = H + 5 log Δ + 2.5·slope·log r).</param>
/// <param name="Designation">"C/2023 A3", "2P", "(4)".</param>
/// <param name="Name">"Tsuchinshan-ATLAS", "Encke", "Vesta"; empty when it has none.</param>
/// <param name="Source">Who published the elements, e.g. "MPC".</param>
public sealed record SmallBodyOrbit(
    SmallBodyKind Kind,
    OrbitalElements Elements,
    double? AbsoluteMagnitude,
    double? Slope,
    string Designation,
    string Name = "",
    string Source = "MPC");

/// <summary>Orbits downloaded from a service, and when (old when they come from the cache while offline).</summary>
public sealed record SmallBodyCatalog(IReadOnlyList<SmallBodyOrbit> Orbits, DateTime FetchedAtUtc);

/// <summary>A comet or asteroid seen from the Earth's centre at an instant.</summary>
/// <param name="AstrometricJ2000">Geocentric astrometric position (light time applied), J2000.</param>
/// <param name="Equatorial">The same, precessed to the equinox of date (what the rest of the app uses).</param>
/// <param name="Magnitude">Total visual magnitude, or NaN when the elements carry no magnitude.</param>
public sealed record SmallBodyPosition(
    EquatorialCoordinates AstrometricJ2000,
    EquatorialCoordinates Equatorial,
    double DistanceAu,
    double SunDistanceAu,
    double ElongationDegrees,
    double PhaseAngleDegrees,
    double Magnitude);
