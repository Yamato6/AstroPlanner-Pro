using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Application.Services;

/// <summary>No survey and no tile cache: the survey layer of a host without internet services (tests, tools).</summary>
public sealed class NoSurveyTiles : ISurveyTileSource, ISizedCache
{
    public static NoSurveyTiles Instance { get; } = new();

    private NoSurveyTiles()
    {
    }

    public IReadOnlyList<HipsSurvey> Surveys => [];

    public Task<HipsTile> GetTileAsync(HipsSurvey survey, HipsTileKey key, CancellationToken cancellationToken = default) =>
        throw new OnlineServiceException("No survey is available.", null);

    public HipsTile? GetCachedTile(HipsSurvey survey, HipsTileKey key) => null;

    public string CreditFor(HipsSurvey survey) => string.Empty;

    public long LimitBytes { get; set; } = SurveyPreferences.DefaultCacheLimitBytes;

    public CachedResponse? Read(string service, string key) => null;

    public void Write(string service, string key, byte[] content, DateTime storedAtUtc)
    {
    }

    public long SizeBytes => 0;

    public void Clear()
    {
    }
}
