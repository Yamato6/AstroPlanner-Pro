using AstroPlanner.Application.Services;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The <c>search.*</c> area, for the planetarium's command palette: <c>search.query</c> matches the
/// catalog the way the classic search does (ids and names), <c>search.go</c> selects a match and
/// turns the view to it, and <c>search.resolve</c> runs the classic search, which falls back to
/// CDS Sesame online when allowed.
/// </summary>
public sealed class SearchEndpoints(ShellViewModel shell, SelectionEndpoints selection) : IEndpointModule
{
    private readonly TargetsViewModel _targets = (shell ?? throw new ArgumentNullException(nameof(shell))).Targets;
    private readonly SkyViewModel _sky = shell.Sky;
    private readonly SelectionEndpoints _selection = selection ?? throw new ArgumentNullException(nameof(selection));

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        rpc.Register(AppMethods.SearchQuery, (request, _) => Task.FromResult(Query(request.Text, request.Limit)));
        rpc.Register(AppMethods.SearchGo, (request, _) =>
        {
            if (_targets.SelectById(request.Id))
            {
                _sky.CenterOnSelectionCommand.Execute(null);
            }

            return Task.FromResult(_selection.Snapshot());
        });
        rpc.Register(AppMethods.SearchResolve, async (request, _) => await ResolveAsync(request.Text));
    }

    /// <summary>
    /// The best matches: the exact id or name first, then ids that start with the text, then the
    /// brightest objects.
    /// </summary>
    public SearchResults Query(string text, int limit)
    {
        var query = CatalogSearch.Parse(text);
        if (query.IsEmpty)
        {
            return new SearchResults([]);
        }

        var trimmed = text.Trim();
        var matches = _targets.AllObjects
            .Where(row => CatalogSearch.Matches(row, query))
            .OrderBy(row => Rank(row, trimmed))
            .ThenBy(row => double.IsNaN(row.ApparentMagnitude) ? double.MaxValue : row.ApparentMagnitude)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 50));
        return new SearchResults([.. matches.Select(row => new SearchResult(
            Cut(row.Id, 64)!,
            Cut(row.Title, 200)!,
            Cut(DisplayNames.Of(row.Type), 80),
            double.IsNaN(row.ApparentMagnitude) ? null : Cut(row.MagnitudeText, 16),
            Cut(row.Constellation, 80)))]);
    }

    private static int Rank(CelestialObjectRowViewModel row, string text) =>
        string.Equals(row.Id, text, StringComparison.OrdinalIgnoreCase) || string.Equals(row.Name, text, StringComparison.OrdinalIgnoreCase) ? 0
        : row.Id.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1
        : 2;

    /// <summary>The classic search: a catalog match is selected and centred, a missing name is resolved online.</summary>
    private async Task<SearchResolveResponse> ResolveAsync(string text)
    {
        var search = _sky.Search;
        search.SearchText = text;
        await search.SearchCommand.ExecuteAsync(null);
        if (search.SearchMessage is { Length: > 0 } message)
        {
            return new SearchResolveResponse(search.ResolvedTarget is not null, Cut(message, 500)!);
        }

        // Found in the catalog: selected and centred, no message.
        return new SearchResolveResponse(true, Cut(_targets.SelectedObject?.Title ?? text, 500)!);
    }

    private static string? Cut(string? text, int length) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= length ? text : text[..length];

    public void Dispose()
    {
    }
}
