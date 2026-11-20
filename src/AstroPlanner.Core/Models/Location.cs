namespace AstroPlanner.Core.Models;

/// <summary>
/// A saved observing site. Time zones are stored with both ids: IANA (portable, used in files
/// and by online services) and Windows (what <see cref="TimeZoneInfo"/> uses on this platform
/// without ICU).
/// </summary>
public sealed record Location
{
    public Location(
        string id,
        string name,
        double latitudeDegrees,
        double longitudeDegrees,
        double elevationMeters,
        string ianaTimeZoneId,
        string windowsTimeZoneId,
        HorizonMask? horizonMask = null,
        string notes = "",
        double? zenithSqm = null)
    {
        Id = RequireText(id, nameof(id));
        Name = RequireText(name, nameof(name));

        // Validates the coordinates.
        _ = new ObserverLocation(latitudeDegrees, longitudeDegrees, elevationMeters);

        LatitudeDegrees = latitudeDegrees;
        LongitudeDegrees = longitudeDegrees;
        ElevationMeters = elevationMeters;
        IanaTimeZoneId = RequireText(ianaTimeZoneId, nameof(ianaTimeZoneId));
        WindowsTimeZoneId = RequireText(windowsTimeZoneId, nameof(windowsTimeZoneId));
        HorizonMask = horizonMask ?? HorizonMask.Flat;
        Notes = notes ?? string.Empty;
        ZenithSqm = zenithSqm is null ? null : ValidateSqm(zenithSqm.Value);
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public double LatitudeDegrees { get; init; }
    public double LongitudeDegrees { get; init; }
    public double ElevationMeters { get; init; }
    public string IanaTimeZoneId { get; init; }
    public string WindowsTimeZoneId { get; init; }
    public HorizonMask HorizonMask { get; init; }
    public string Notes { get; init; }

    /// <summary>Measured zenith sky brightness (SQM, mag/arcsec²) typed by the user, or null (spec §4.8).</summary>
    public double? ZenithSqm { get; init; }

    public ObserverLocation ToObserver() => new(LatitudeDegrees, LongitudeDegrees, ElevationMeters);

    private static double ValidateSqm(double sqm) =>
        double.IsFinite(sqm) && sqm is >= SkyQuality.SkyBrightness.MinimumSqm and <= SkyQuality.SkyBrightness.MaximumSqm
            ? sqm
            : throw new ArgumentOutOfRangeException(nameof(sqm), "The SQM reading must be between 16 and 22.5 mag/arcsec².");

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : value.Trim();
}
