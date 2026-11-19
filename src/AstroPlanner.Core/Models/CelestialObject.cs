namespace AstroPlanner.Core.Models;

public enum CelestialObjectType
{
    Galaxy,
    OpenCluster,
    GlobularCluster,
    PlanetaryNebula,
    DiffuseNebula,
    SupernovaRemnant,
    /// <summary>
    /// For Messier objects that do not fit the standard categories (e.g. M40, a double star;
    /// M73, a group of stars that is not a confirmed physical cluster).
    /// </summary>
    Unknown,
    /// <summary>Planet of the Solar System: its position and magnitude change with the date.</summary>
    Planet
}

public sealed class CelestialObject
{
    /// <summary>Constellation shown for planets (their real one changes with the date).</summary>
    public const string SolarSystemConstellation = "Solar System";

    public CelestialObject(
        string id,
        string commonName,
        string constellation,
        CelestialObjectType type,
        double apparentMagnitude,
        EquatorialCoordinates coordinates)
    {
        Id = RequireText(id, nameof(id));
        CommonName = RequireText(commonName, nameof(commonName));
        Constellation = RequireText(constellation, nameof(constellation));
        Type = type;
        ApparentMagnitude = apparentMagnitude;
        Coordinates = coordinates ?? throw new ArgumentNullException(nameof(coordinates));
    }

    private CelestialObject(string id, string commonName, Planet planet)
        : this(id, commonName, SolarSystemConstellation, CelestialObjectType.Planet, double.NaN,
            new EquatorialCoordinates(0, 0))
    {
        Planet = planet;
    }

    /// <summary>
    /// Creates a planet. Its <see cref="Coordinates"/> and <see cref="ApparentMagnitude"/> are
    /// not used: they depend on the date and <c>IAstronomyService</c> calculates them.
    /// </summary>
    public static CelestialObject CreatePlanet(string id, string commonName, Planet planet) =>
        new(id, commonName, planet);

    public string Id { get; }
    public string CommonName { get; }
    public string Constellation { get; }
    public CelestialObjectType Type { get; }

    /// <summary>Catalog magnitude (NaN for planets, whose magnitude depends on the date).</summary>
    public double ApparentMagnitude { get; }

    /// <summary>Catalog J2000 coordinates (unused for planets).</summary>
    public EquatorialCoordinates Coordinates { get; }

    /// <summary>The planet, or null for a deep-sky object with fixed coordinates.</summary>
    public Planet? Planet { get; }

    public bool IsPlanet => Planet is not null;

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : value.Trim();
}
