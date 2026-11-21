using AstroPlanner.Core.Models;

namespace AstroPlanner.DataBuilder;

/// <summary>A cross-identification of an object.</summary>
/// <param name="Catalog">Catalog prefix: "M", "NGC", "IC", "C", "PGC"…</param>
internal sealed record Designation(string Value, string Catalog);

/// <summary>A deep-sky object ready to be written to <c>catalogs.sqlite</c>.</summary>
internal sealed record CatalogEntry
{
    public required string Id { get; init; }
    public required CelestialObjectType Type { get; init; }
    public required double RightAscensionHours { get; init; }
    public required double DeclinationDegrees { get; init; }

    /// <summary>IAU abbreviation computed from the position.</summary>
    public required string Constellation { get; init; }

    public double? Magnitude { get; init; }

    /// <summary>"V" or "B".</summary>
    public string? MagnitudeBand { get; init; }

    public double? SurfaceBrightness { get; init; }
    public double? MajorAxisArcmin { get; init; }
    public double? MinorAxisArcmin { get; init; }
    public double? PositionAngleDegrees { get; init; }
    public string CommonName { get; init; } = string.Empty;
    public List<Designation> Designations { get; init; } = [];
    public List<string> AlternateNames { get; init; } = [];

    /// <summary>Source row, e.g. "OpenNGC:NGC0224".</summary>
    public required string Source { get; init; }
}

/// <summary>A star ready to be written to <c>catalogs.sqlite</c>.</summary>
internal sealed record StarEntry(
    int HygId,
    int? Hip,
    int? Hd,
    int? Hr,
    string? ProperName,
    string? Bayer,
    int? Flamsteed,
    string Constellation,
    double RightAscensionHours,
    double DeclinationDegrees,
    double Magnitude,
    double? ColorIndex,
    double? ProperMotionRaMas,
    double? ProperMotionDecMas)
{
    /// <summary>MK spectral type as HYG gives it (e.g. "A0m..."), null when unknown.</summary>
    public string? SpectralType { get; init; }

    /// <summary>Distance in parsecs, null when unknown (HYG's 100000).</summary>
    public double? DistanceParsecs { get; init; }

    /// <summary>Variable-star designation (e.g. "Alp CMa" style or a GCVS name), null when not variable.</summary>
    public string? VariableName { get; init; }

    /// <summary>Magnitude range of a variable: brightest (<see cref="VariableMinimumMagnitude"/>) and faintest.</summary>
    public double? VariableMinimumMagnitude { get; init; }

    public double? VariableMaximumMagnitude { get; init; }

    /// <summary>HYG multiple-system grouping: the component number, its primary's HYG id and the system's base name.</summary>
    public int? Component { get; init; }

    public int? ComponentPrimary { get; init; }

    public string? MultipleSystem { get; init; }
}
