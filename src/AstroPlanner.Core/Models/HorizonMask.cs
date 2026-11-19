namespace AstroPlanner.Core.Models;

/// <summary>A point of a horizon profile: the local horizon is at <paramref name="AltitudeDegrees"/> towards <paramref name="AzimuthDegrees"/>.</summary>
public sealed record HorizonPoint(double AzimuthDegrees, double AltitudeDegrees);

/// <summary>
/// Local horizon profile (trees, buildings, hills): the minimum altitude at which the sky is
/// visible, as a function of azimuth. Between points it is interpolated linearly, wrapping
/// around north. An empty mask is a flat horizon at 0°.
/// </summary>
public sealed class HorizonMask : IEquatable<HorizonMask>
{
    public static readonly HorizonMask Flat = new([]);

    private readonly HorizonPoint[] _points;

    public HorizonMask(IEnumerable<HorizonPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        _points = [.. points
            .Select(Validate)
            .Select(p => p with { AzimuthDegrees = p.AzimuthDegrees % 360.0 })
            .OrderBy(p => p.AzimuthDegrees)];

        for (var index = 1; index < _points.Length; index++)
        {
            if (_points[index].AzimuthDegrees == _points[index - 1].AzimuthDegrees)
            {
                throw new ArgumentException("Two horizon points have the same azimuth.", nameof(points));
            }
        }
    }

    /// <summary>Points sorted by azimuth (0 ≤ azimuth &lt; 360).</summary>
    public IReadOnlyList<HorizonPoint> Points => _points;

    public bool IsFlat => _points.Length == 0 || _points.All(p => p.AltitudeDegrees <= 0);

    /// <summary>Altitude of the local horizon towards <paramref name="azimuthDegrees"/>.</summary>
    public double AltitudeAt(double azimuthDegrees)
    {
        switch (_points.Length)
        {
            case 0:
                return 0;
            case 1:
                return _points[0].AltitudeDegrees;
        }

        var azimuth = ((azimuthDegrees % 360.0) + 360.0) % 360.0;

        // Find the segment [previous, next] that contains the azimuth, wrapping around 360°.
        var nextIndex = Array.FindIndex(_points, p => p.AzimuthDegrees >= azimuth);
        HorizonPoint previous, next;
        double span, offset;
        if (nextIndex is 0 or -1)
        {
            previous = _points[^1];
            next = _points[0];
            span = next.AzimuthDegrees + 360.0 - previous.AzimuthDegrees;
            offset = azimuth >= previous.AzimuthDegrees
                ? azimuth - previous.AzimuthDegrees
                : azimuth + 360.0 - previous.AzimuthDegrees;
        }
        else
        {
            previous = _points[nextIndex - 1];
            next = _points[nextIndex];
            span = next.AzimuthDegrees - previous.AzimuthDegrees;
            offset = azimuth - previous.AzimuthDegrees;
        }

        return previous.AltitudeDegrees + (next.AltitudeDegrees - previous.AltitudeDegrees) * (offset / span);
    }

    public bool Equals(HorizonMask? other) =>
        other is not null && _points.SequenceEqual(other._points);

    public override bool Equals(object? obj) => Equals(obj as HorizonMask);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in _points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    private static HorizonPoint Validate(HorizonPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (double.IsNaN(point.AzimuthDegrees) || point.AzimuthDegrees is < 0 or > 360)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Azimuth must be between 0 and 360 degrees.");
        }

        if (double.IsNaN(point.AltitudeDegrees) || point.AltitudeDegrees is < -10 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Horizon altitude must be between -10 and 90 degrees.");
        }

        return point;
    }
}
