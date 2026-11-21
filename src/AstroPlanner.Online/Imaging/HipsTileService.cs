using System.Globalization;
using System.Net.Http;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Http;

namespace AstroPlanner.Online.Imaging;

/// <summary>
/// HiPS survey tiles for the framing preview (spec §4.4, §9). Each survey lists its mirrors in order
/// of preference; a mirror that fails is skipped for <see cref="MirrorPause"/>. Tiles never change,
/// so they stay in the disk cache until it is trimmed or cleared. At most
/// <see cref="MaximumParallelDownloads"/> downloads run at once. Only sky coordinates (tile numbers)
/// are sent, nothing about the user.
/// </summary>
public sealed class HipsTileService(HttpClient http, IOnlineCache? cache = null, TimeProvider? timeProvider = null)
    : ISurveyTileSource, IDisposable
{
    public const string CacheName = "hips";
    public const int MaximumParallelDownloads = 4;

    /// <summary>A mirror that failed is not asked again for this long.</summary>
    public static readonly TimeSpan MirrorPause = TimeSpan.FromMinutes(10);

    /// <summary>"Not found" answers in a row after which a mirror is considered down (a whole mirror answering 404).</summary>
    public const int NotFoundLimit = 3;

    private static readonly TimeSpan TileCacheDuration = TimeSpan.FromDays(3650);

    /// <summary>A survey, its credit and its mirrors (base URLs without a trailing slash).</summary>
    public sealed record Definition(HipsSurvey Survey, string Credit, IReadOnlyList<(string Name, string BaseUrl)> Mirrors);

    /// <summary>The surveys offered (first = default). DSS2 colour is mirrored by ESA ESASky; Pan-STARRS only by CDS.</summary>
    public static readonly IReadOnlyList<Definition> Definitions =
    [
        new(new HipsSurvey("DSSColor", "DSS2 colour", 9),
            "DSS: STScI/NASA, plates © Caltech/Palomar, UK STFC/ROE, AAO (CC BY-NC-SA 4.0) · colour and HiPS by CDS",
            [("CDS", "https://alasky.cds.unistra.fr/DSS/DSSColor"), ("ESA ESASky", "https://skies.esac.esa.int/DSSColor")]),
        new(new HipsSurvey("PanSTARRS-DR1-color", "Pan-STARRS DR1 colour", 11),
            "Pan-STARRS1: STScI/MAST · HiPS by CDS",
            [("CDS", "https://alasky.cds.unistra.fr/Pan-STARRS/DR1/color-z-zg-g")]),
    ];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _downloads = new(MaximumParallelDownloads);
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (DateTime PausedUntilUtc, int NotFoundInARow)> _mirrors = [];

    public IReadOnlyList<HipsSurvey> Surveys { get; } = [.. Definitions.Select(d => d.Survey)];

    public string CreditFor(HipsSurvey survey) => Find(survey).Credit;

    /// <summary>Tile path relative to the survey root, as HiPS 1.0 defines it.</summary>
    public static string TilePath(HipsTileKey key) => key.IsAllsky
        ? FormattableString.Invariant($"Norder{key.Order}/Allsky.jpg")
        : FormattableString.Invariant($"Norder{key.Order}/Dir{key.Index / 10000 * 10000}/Npix{key.Index}.jpg");

    public async Task<HipsTile> GetTileAsync(HipsSurvey survey, HipsTileKey key, CancellationToken cancellationToken = default)
    {
        var definition = Find(survey);
        var cacheKey = survey.Id + "/" + TilePath(key);
        var now = _time.GetUtcNow().UtcDateTime;
        if (cache?.Read(CacheName, cacheKey) is { Content.Length: > 0 } cached && now - cached.StoredAtUtc < TileCacheDuration)
        {
            return new HipsTile(cached.Content, "cache");
        }

        Exception? last = null;
        foreach (var (name, baseUrl) in definition.Mirrors)
        {
            if (IsPaused(baseUrl, now))
            {
                continue;
            }

            await _downloads.WaitAsync(cancellationToken);
            try
            {
                var content = await CachedFetch.FetchAsync(http, baseUrl + "/" + TilePath(key), name, Fail, notFoundIsEmpty: true, cancellationToken);
                if (content.Length == 0)
                {
                    Record(baseUrl, now, notFound: true);
                    last = Fail(string.Create(CultureInfo.InvariantCulture, $"{name} has no tile {TilePath(key)}."), null);
                    continue;
                }

                Record(baseUrl, now, notFound: false, success: true);
                cache?.Write(CacheName, cacheKey, content, now);
                return new HipsTile(content, name);
            }
            catch (Exception ex) when (CachedFetch.IsServiceFailure(ex, cancellationToken))
            {
                Record(baseUrl, now, notFound: false);
                last = ex;
            }
            finally
            {
                _downloads.Release();
            }
        }

        throw last as OnlineServiceException ?? Fail("Survey images are unavailable (no connection).", last);
    }

    public void Dispose() => _downloads.Dispose();

    private static OnlineServiceException Fail(string message, Exception? inner) => new(message, inner);

    private static Definition Find(HipsSurvey survey) =>
        Definitions.FirstOrDefault(d => d.Survey.Id == survey.Id)
        ?? throw new ArgumentException($"Unknown survey {survey.Id}.", nameof(survey));

    private bool IsPaused(string mirror, DateTime now)
    {
        lock (_lock)
        {
            return _mirrors.TryGetValue(mirror, out var state) && now < state.PausedUntilUtc;
        }
    }

    private void Record(string mirror, DateTime now, bool notFound, bool success = false)
    {
        lock (_lock)
        {
            _mirrors.TryGetValue(mirror, out var state);
            if (success)
            {
                _mirrors[mirror] = (DateTime.MinValue, 0);
            }
            else if (notFound)
            {
                var count = state.NotFoundInARow + 1;
                _mirrors[mirror] = (count >= NotFoundLimit ? now + MirrorPause : state.PausedUntilUtc, count >= NotFoundLimit ? 0 : count);
            }
            else
            {
                _mirrors[mirror] = (now + MirrorPause, 0);
            }
        }
    }
}
