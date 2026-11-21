namespace AstroPlanner.Core.Models;

/// <summary>
/// A pair of a double or multiple star (Washington Double Star Catalog), as last measured.
/// </summary>
/// <param name="WdsId">WDS identifier, J2000 position (e.g. "19307+2758").</param>
/// <param name="Discoverer">Discoverer code and number (e.g. "STFA 43").</param>
/// <param name="Components">Components (e.g. "AB"); empty for a plain pair.</param>
/// <param name="LastYear">Year of the measure the angle and separation are from.</param>
/// <param name="PositionAngleDegrees">Position angle of the companion, from north through east.</param>
public sealed record DoubleStarPair(
    string WdsId,
    string Discoverer,
    string Components,
    int LastYear,
    double? PositionAngleDegrees,
    double SeparationArcsec,
    double PrimaryMagnitude,
    double CompanionMagnitude)
{
    /// <summary>The discoverer code without the WDS column padding: "AGC   1" → "AGC 1".</summary>
    public string DiscovererName => string.Join(' ', Discoverer.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>"STFA 43 AB".</summary>
    public string Name => Components.Length == 0 ? DiscovererName : $"{DiscovererName} {Components}";

    /// <summary>A pair inside an unresolved component (e.g. "Aa,Ab"), usually found by speckle or spectroscopy.</summary>
    public bool IsClose => Components.Contains(',', StringComparison.Ordinal) && Components.Any(char.IsLower);
}

/// <summary>
/// What the catalog knows about a star (HYG database), and its pairs when it is a double (WDS).
/// </summary>
/// <param name="HygId">HYG id, also the id of the star in the sky renderer.</param>
/// <param name="SpectralType">MK spectral type (e.g. "A0m..."), null when unknown.</param>
/// <param name="ColorIndex">B−V colour index.</param>
/// <param name="VariableFaintestMagnitude">Faintest magnitude of a variable, as HYG gives it (Hipparcos photometry, not the full historical range).</param>
public sealed record StarInfo(
    int HygId,
    int? Hip,
    int? Hd,
    string? SpectralType,
    double? ColorIndex,
    double? DistanceParsecs,
    string? VariableName,
    double? VariableFaintestMagnitude,
    double? VariableBrightestMagnitude)
{
    /// <summary>Light-years per parsec.</summary>
    public const double LightYearsPerParsec = 3.261563777;

    /// <summary>The double-star pairs whose primary is this star, closest first (empty for a single star).</summary>
    public IReadOnlyList<DoubleStarPair> Pairs { get; init; } = [];

    public bool IsDouble => Pairs.Count > 0;

    public bool IsVariable => VariableName is not null;

    public double? DistanceLightYears => DistanceParsecs * LightYearsPerParsec;

    /// <summary>
    /// The pair the star is known for: the brightest companion among the pairs that are not inside
    /// an unresolved component and are at least 1″ apart (else the brightest companion of all).
    /// </summary>
    public DoubleStarPair? MainPair =>
        Pairs.Where(p => !p.IsClose && p.SeparationArcsec >= 1).OrderBy(p => p.CompanionMagnitude).ThenBy(p => p.SeparationArcsec).FirstOrDefault()
        ?? Pairs.OrderBy(p => p.CompanionMagnitude).ThenBy(p => p.SeparationArcsec).FirstOrDefault();
}
