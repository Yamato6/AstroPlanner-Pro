using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Http;

namespace AstroPlanner.Online.SmallBodies;

/// <summary>
/// Comet and bright-asteroid elements from the Minor Planet Center (spec §9.8): the daily
/// <c>CometEls.txt</c> (fixed columns, "CometOrbitFormat") and the yearly bright-asteroid file in
/// MPCORB format. Nothing about the user is sent. Cached for <see cref="CometCacheDuration"/> and
/// <see cref="AsteroidCacheDuration"/>, and used from the cache when the MPC cannot be reached.
/// </summary>
public sealed partial class MpcSmallBodyService(HttpClient http, TimeProvider? timeProvider = null, IOnlineCache? cache = null)
    : ISmallBodyService
{
    public const string CometsUrl = "https://minorplanetcenter.net/iau/MPCORB/CometEls.txt";
    public const string CacheName = "mpc";
    public static readonly TimeSpan CometCacheDuration = TimeSpan.FromHours(24);
    public static readonly TimeSpan AsteroidCacheDuration = TimeSpan.FromDays(30);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>The bright-asteroid file of <paramref name="year"/> (published at the start of each year).</summary>
    public static string BrightAsteroidsUrl(int year) =>
        string.Create(CultureInfo.InvariantCulture, $"https://minorplanetcenter.net/iau/Ephemerides/Bright/{year}/Soft00Bright.txt");

    public async Task<SmallBodyCatalog> GetCometsAsync(CancellationToken cancellationToken = default)
    {
        var fetched = await CachedFetch.GetAsync(http, cache, CacheName, CometsUrl, CometCacheDuration, Now, "the Minor Planet Center",
            Fail, cancellationToken);
        return new SmallBodyCatalog(ParseComets(Text(fetched.Content)), fetched.FetchedAtUtc);
    }

    public async Task<SmallBodyCatalog> GetBrightAsteroidsAsync(CancellationToken cancellationToken = default)
    {
        // This year's file, or last year's early in January before the new one is out.
        var year = Now.Year;
        var fetched = await CachedFetch.GetAsync(http, cache, CacheName, "bright-asteroids", [BrightAsteroidsUrl(year), BrightAsteroidsUrl(year - 1)],
            AsteroidCacheDuration, Now, "the Minor Planet Center", Fail, cancellationToken);
        return new SmallBodyCatalog(ParseBrightAsteroids(Text(fetched.Content)), fetched.FetchedAtUtc);
    }

    /// <summary>
    /// Comets of <c>CometEls.txt</c>. Lines that cannot be read (truncated, missing numbers) are
    /// skipped; a comet without magnitude parameters keeps H = null.
    /// </summary>
    public static IReadOnlyList<SmallBodyOrbit> ParseComets(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var comets = new List<SmallBodyOrbit>();
        foreach (var line in Lines(text))
        {
            if (line.Length < 102
                || !Int(line, 15, 18, out var year) || !Int(line, 20, 21, out var month) || !Number(line, 23, 29, out var day)
                || !Number(line, 31, 39, out var q) || !Number(line, 42, 49, out var e)
                || !Number(line, 52, 59, out var peri) || !Number(line, 62, 69, out var node) || !Number(line, 72, 79, out var incl)
                || q <= 0 || e < 0 || month is < 1 or > 12)
            {
                continue;
            }

            var epoch = Int(line, 82, 85, out var ey) && Int(line, 86, 87, out var em) && Int(line, 88, 89, out var ed)
                ? Jd(ey, em, ed)
                : Jd(year, month, day);
            double? h = Number(line, 92, 95, out var hValue) ? hValue : null;
            double? slope = Number(line, 97, 100, out var slopeValue) ? slopeValue : null;
            var (designation, name) = SplitCometName(Field(line, 103, 158));
            if (designation.Length == 0)
            {
                continue;
            }

            comets.Add(new SmallBodyOrbit(SmallBodyKind.Comet,
                new OrbitalElements(q, e, Jd(year, month, day), peri, node, incl, epoch), h, slope, designation, name));
        }

        return comets;
    }

    /// <summary>Asteroids of an MPCORB-format file (the header lines are skipped).</summary>
    public static IReadOnlyList<SmallBodyOrbit> ParseBrightAsteroids(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var asteroids = new List<SmallBodyOrbit>();
        foreach (var line in Lines(text))
        {
            if (line.Length < 103
                || UnpackEpoch(Field(line, 21, 25)) is not { } epoch
                || !Number(line, 27, 35, out var meanAnomaly) || !Number(line, 38, 46, out var peri)
                || !Number(line, 49, 57, out var node) || !Number(line, 60, 68, out var incl)
                || !Number(line, 71, 79, out var e) || !Number(line, 93, 103, out var a)
                || a <= 0 || e is < 0 or >= 1)
            {
                continue;
            }

            double? h = Number(line, 9, 13, out var hValue) ? hValue : null;
            double? g = Number(line, 15, 19, out var gValue) ? gValue : null;
            var readable = line.Length >= 167 ? Field(line, 167, Math.Min(line.Length, 194)) : Field(line, 1, 7);
            var (designation, name) = SplitAsteroidName(readable);
            asteroids.Add(new SmallBodyOrbit(SmallBodyKind.Asteroid,
                OrbitalElements.FromMeanAnomaly(a, e, incl, node, peri, meanAnomaly, epoch), h, g, designation, name));
        }

        return asteroids;
    }

    /// <summary>"2P/Encke" → ("2P", "Encke"); "C/2023 A3 (Tsuchinshan-ATLAS)" → ("C/2023 A3", "Tsuchinshan-ATLAS").</summary>
    public static (string Designation, string Name) SplitCometName(string field)
    {
        var text = field.Trim();
        if (PeriodicName().Match(text) is { Success: true } periodic)
        {
            return (periodic.Groups[1].Value, periodic.Groups[2].Value.Trim());
        }

        if (ProvisionalName().Match(text) is { Success: true } provisional)
        {
            return (provisional.Groups[1].Value, provisional.Groups[2].Value.Trim());
        }

        return (text, "");
    }

    /// <summary>"(4) Vesta" → ("(4)", "Vesta"); "(433) Eros" → ("(433)", "Eros").</summary>
    public static (string Designation, string Name) SplitAsteroidName(string readable)
    {
        var text = readable.Trim();
        var close = text.IndexOf(')', StringComparison.Ordinal);
        return text.StartsWith('(') && close > 0 ? (text[..(close + 1)], text[(close + 1)..].Trim()) : (text, "");
    }

    /// <summary>MPC packed date "K25BL" → JD of 2025-11-21.0; null when invalid.</summary>
    public static double? UnpackEpoch(string packed)
    {
        if (packed.Length != 5)
        {
            return null;
        }

        var century = packed[0] switch { 'I' => 1800, 'J' => 1900, 'K' => 2000, 'L' => 2100, _ => -1 };
        if (century < 0 || !int.TryParse(packed.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        var month = Unpack(packed[3]);
        var day = Unpack(packed[4]);
        return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(century + year, month)
            ? Jd(century + year, month, day)
            : null;

        static int Unpack(char c) => c is >= '1' and <= '9' ? c - '0' : c is >= 'A' and <= 'V' ? c - 'A' + 10 : -1;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static OnlineServiceException Fail(string message, Exception? inner) => new(message, inner);

    private static string Text(byte[] content) => Encoding.UTF8.GetString(content);

    private static IEnumerable<string> Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r'));

    private static double Jd(int year, int month, double day) =>
        AstronomyMath.ToJulianDate(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc)) + day - 1;

    /// <summary>Columns <paramref name="first"/>–<paramref name="last"/>, 1-based and inclusive, as in the MPC documentation.</summary>
    private static string Field(string line, int first, int last) =>
        first > line.Length ? "" : line.Substring(first - 1, Math.Min(last, line.Length) - first + 1).Trim();

    private static bool Number(string line, int first, int last, out double value) =>
        double.TryParse(Field(line, first, last), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool Int(string line, int first, int last, out int value) =>
        int.TryParse(Field(line, first, last), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    [GeneratedRegex(@"^(\d+[PDI](?:-[A-Z]+)?)/(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PeriodicName();

    [GeneratedRegex(@"^([PCDXIA]/-?\d{1,4} [A-Z]{1,2}\d*(?:-[A-Z]+)?)\s*(?:\((.*)\))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProvisionalName();
}
