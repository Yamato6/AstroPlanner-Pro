using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.Services;

namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// The sky's search box: finds an object by id or name ("m31", "ngc 7000", "Orion", "Jupiter"),
/// selects it and centres the view. A name missing from the catalog is resolved online with CDS
/// Sesame when allowed (spec §4.2): the view turns to it and the message says what it is.
/// </summary>
public sealed partial class SkySearchViewModel : ObservableObject
{
    private readonly SkySession _session;
    private readonly Func<IEnumerable<CelestialObjectRowViewModel>> _rows;
    private readonly Func<string, bool> _select;
    private readonly Action<CelestialObject, double?> _centerOn;
    private readonly INameResolver? _nameResolver;
    private readonly OnlineInfoState? _online;

    /// <param name="rows">The whole catalog (not only the filtered rows).</param>
    /// <param name="select">Selects a row by id in Targets; false if it is not there.</param>
    /// <param name="centerOn">Turns the view to an object with a field of view (null keeps the field).</param>
    internal SkySearchViewModel(SkySession session, Func<IEnumerable<CelestialObjectRowViewModel>> rows, Func<string, bool> select,
        Action<CelestialObject, double?> centerOn, INameResolver? nameResolver, OnlineInfoState? online)
    {
        _session = session;
        _rows = rows;
        _select = select;
        _centerOn = centerOn;
        _nameResolver = nameResolver;
        _online = online;
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string? SearchMessage { get; private set; }

    /// <summary>The last name resolved online (Sesame), shown at the centre of the view; null otherwise.</summary>
    [ObservableProperty]
    public partial ResolvedName? ResolvedTarget { get; private set; }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var text = SearchText.Trim();
        var query = CatalogSearch.Parse(SearchText);
        if (query.IsEmpty)
        {
            return;
        }

        ResolvedTarget = null;

        // An exact name ("Jupiter", "Andromeda Galaxy") wins over objects that only contain the text.
        var matches = _rows().Where(item => CatalogSearch.Matches(item, query)).ToList();
        var match = matches.FirstOrDefault(item => string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase))
                    ?? matches.FirstOrDefault();
        if (match is not null)
        {
            SearchMessage = null;
            _select(match.Id);
            _centerOn(match.Target, match.IsPlanet ? 20 : match.Target.IsSmallBody ? 10 : 30);
            return;
        }

        if (_nameResolver is null || _online is { NameResolver.Enabled: false })
        {
            SearchMessage = DisplayNames.Format(Strings.SkySearchNotFoundFormat, text);
            return;
        }

        SearchMessage = DisplayNames.Format(Strings.SkySearchResolvingFormat, text);
        try
        {
            var resolved = await _nameResolver.ResolveAsync(text);
            ReportResolver(null);
            if (SearchText.Trim() != text)
            {
                return;   // the user typed something else meanwhile
            }

            if (resolved is null)
            {
                SearchMessage = DisplayNames.Format(Strings.SkySearchNotFoundOnlineFormat, text);
                return;
            }

            ResolvedTarget = resolved;
            SearchMessage = DisplayNames.Format(Strings.SkySearchResolvedFormat, resolved.Name, resolved.ObjectType ?? "?", resolved.Source,
                resolved.Coordinates.RightAscensionHours, resolved.Coordinates.DeclinationDegrees);
            var direction = SkyBridgeMapper.ToVector(SkyFrame.FromEquatorial(resolved.Coordinates));
            _session.Send(SkyMessageCodec.CenterOn(new CenterOnPayload(direction.X, direction.Y, direction.Z, 5)));
        }
        catch (OnlineServiceException ex)
        {
            ReportResolver(ex.Message);
            SearchMessage = DisplayNames.Format(Strings.SkySearchOnlineFailedFormat, text, ex.Message);
        }
    }

    private void ReportResolver(string? error)
    {
        if (_online is not null)
        {
            _online.Report(_online.NameResolver, _session.Time.GetUtcNow().UtcDateTime, error);
        }
    }
}
