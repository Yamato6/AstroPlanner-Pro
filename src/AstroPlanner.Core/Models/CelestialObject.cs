namespace AstroPlanner.Core.Models;

/// <summary>Kind of object. New values are appended so that stored names stay valid.</summary>
public enum CelestialObjectType
{
    Galaxy,
    OpenCluster,
    GlobularCluster,
    PlanetaryNebula,

    /// <summary>Nebula of unspecified kind (OpenNGC "Neb").</summary>
    DiffuseNebula,
    SupernovaRemnant,

    /// <summary>Objects that fit no other category (OpenNGC "Other").</summary>
    Unknown,

    /// <summary>Planet of the Solar System: its position and magnitude change with the date.</summary>
    Planet,

    /// <summary>Galaxy pair, triplet or group.</summary>
    GalaxyGroup,

    /// <summary>Stellar association or asterism (e.g. the Double Cluster entry, Brocchi's Cluster).</summary>
    StellarAssociation,

    /// <summary>Star cluster with associated nebulosity (e.g. M16).</summary>
    ClusterWithNebula,
    HiiRegion,
    EmissionNebula,
    ReflectionNebula,
    DarkNebula,
    Star,
    DoubleStar,
    Nova,
    Comet,
    Asteroid,

    /// <summary>The Milky Way's core (galactic centre region): a wide-field target.</summary>
    MilkyWayCore
}

/// <summary>
/// An object of the catalog. Deep-sky objects have fixed J2000 coordinates; planets are
/// calculated for each instant.
/// </summary>
public sealed class CelestialObject
{
    /// <summary>Constellation shown for planets until their real one is calculated for a date.</summary>
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
        CommonName = commonName?.Trim() ?? string.Empty;
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

    private CelestialObject(string id, string commonName, SmallBodyOrbit orbit)
        : this(id, commonName, SolarSystemConstellation,
            orbit.Kind == SmallBodyKind.Comet ? CelestialObjectType.Comet : CelestialObjectType.Asteroid, double.NaN,
            new EquatorialCoordinates(0, 0))
    {
        Orbit = orbit;
    }

    /// <summary>
    /// Creates a planet. Its <see cref="Coordinates"/> and <see cref="ApparentMagnitude"/> are
    /// not used: they depend on the date and <c>IAstronomyService</c> calculates them.
    /// </summary>
    public static CelestialObject CreatePlanet(string id, string commonName, Planet planet) =>
        new(id, commonName, planet);

    /// <summary>
    /// Creates a comet or asteroid from its orbit. Like a planet, its position and magnitude
    /// depend on the date and <c>IAstronomyService</c> calculates them.
    /// </summary>
    public static CelestialObject CreateSmallBody(string id, string commonName, SmallBodyOrbit orbit)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        return new CelestialObject(id, commonName, orbit);
    }

    /// <summary>
    /// Primary identifier: "M42" for Messier objects, otherwise the catalog name, e.g.
    /// "NGC 7000", "IC 434", "C14".
    /// </summary>
    public string Id { get; }

    /// <summary>Best-known common name; empty when the object has none.</summary>
    public string CommonName { get; }

    /// <summary>Full IAU constellation name (e.g. "Orion").</summary>
    public string Constellation { get; }
    public CelestialObjectType Type { get; }

    /// <summary>Catalog magnitude (V, or B when V is unknown); NaN if unknown or for planets.</summary>
    public double ApparentMagnitude { get; }

    /// <summary>Catalog J2000 coordinates (unused for planets).</summary>
    public EquatorialCoordinates Coordinates { get; }

    /// <summary>The planet, or null for a deep-sky object with fixed coordinates.</summary>
    public Planet? Planet { get; }

    public bool IsPlanet => Planet is not null;

    /// <summary>The comet's or asteroid's orbit, or null.</summary>
    public SmallBodyOrbit? Orbit { get; }

    public bool IsSmallBody => Orbit is not null;

    /// <summary>A Solar System body whose position changes with the date (planet, comet, asteroid).</summary>
    public bool IsMoving => Planet is not null || Orbit is not null;

    /// <summary>Other identifiers of the same object (cross-IDs), e.g. "NGC 1976", "C14", "PGC 2557".</summary>
    public IReadOnlyList<string> Designations { get; init; } = [];

    /// <summary>Other common names besides <see cref="CommonName"/>.</summary>
    public IReadOnlyList<string> AlternateNames { get; init; } = [];

    /// <summary>Apparent major axis, in arcminutes.</summary>
    public double? MajorAxisArcmin { get; init; }

    /// <summary>Apparent minor axis, in arcminutes.</summary>
    public double? MinorAxisArcmin { get; init; }

    /// <summary>Position angle of the major axis, degrees from north through east.</summary>
    public double? PositionAngleDegrees { get; init; }

    /// <summary>Mean surface brightness, mag/arcsec².</summary>
    public double? SurfaceBrightness { get; init; }

    /// <summary>The common name, or the id when there is none.</summary>
    public string DisplayName => CommonName.Length > 0 ? CommonName : Id;

    /// <summary>The id and every designation.</summary>
    public IEnumerable<string> AllIdentifiers => Designations.Prepend(Id);

    /// <summary>Messier number, or null.</summary>
    public int? MessierNumber => CatalogNumber("M");

    /// <summary>Caldwell number, or null.</summary>
    public int? CaldwellNumber => CatalogNumber("C");

    /// <summary>True if the object has an NGC (or IC) number.</summary>
    public bool IsInCatalog(string catalogPrefix) => AllIdentifiers.Any(id => HasPrefix(id, catalogPrefix));

    private int? CatalogNumber(string prefix)
    {
        foreach (var identifier in AllIdentifiers)
        {
            if (HasPrefix(identifier, prefix)
                && int.TryParse(identifier.AsSpan(prefix.Length).Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }
        }

        return null;
    }

    /// <summary>"M42", "M 42" → true for "M"; "MWSC 1" → false (the prefix must be followed by a digit or space+digit).</summary>
    private static bool HasPrefix(string identifier, string prefix)
    {
        if (!identifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || identifier.Length <= prefix.Length)
        {
            return false;
        }

        var rest = identifier.AsSpan(prefix.Length).TrimStart(' ');
        return rest.Length > 0 && char.IsAsciiDigit(rest[0]);
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : value.Trim();
}
