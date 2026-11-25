using System.ComponentModel;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The <c>landscape.*</c> area (spec §8.5, Settings › Sky): which landscape the active saved site
/// shows (<see cref="Core.Models.Location.LandscapeId"/>, saved with the site) and, on request,
/// its horizon copied into the site's horizon mask so that every calculation uses it. Every
/// command answers with the new <see cref="LandscapeSnapshot"/>; <c>landscape.changed</c>
/// announces changes made elsewhere (another site, the classic Locations editor).
/// </summary>
public sealed class LandscapeEndpoints : IEndpointModule
{
    private readonly LocationsViewModel _locations;
    private readonly IObserverContext _context;
    private readonly MediaLibrary _media;
    private readonly ChangeFeed _changes;
    private RpcDispatcher? _rpc;

    public LandscapeEndpoints(LocationsViewModel locations, IObserverContext context, MediaLibrary media, IUiDispatcher ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(media);
        _locations = locations;
        _context = context;
        _media = media;
        _changes = new ChangeFeed(ui, time, revision => _rpc?.Publish(AppEvents.LandscapeChanged, new LandscapeChangedEvent(revision)));
    }

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.LandscapeGet, (_, _) => Task.FromResult(Snapshot()));
        rpc.Register(AppMethods.LandscapeSet, (request, _) =>
        {
            // Only a landscape this media folder has, "flat" or none; anything else is ignored.
            if (request.LandscapeId is null || _media.Resolve(request.LandscapeId) is not null)
            {
                _locations.UpdateActive(location => location with { LandscapeId = request.LandscapeId });
            }

            return Task.FromResult(Snapshot());
        });
        rpc.Register(AppMethods.LandscapeUseHorizon, (_, _) =>
        {
            if (_media.Find(_locations.ActiveLocation?.LandscapeId) is { } landscape)
            {
                _locations.UpdateActive(location => location with { HorizonMask = landscape.ToHorizonMask() });
            }

            return Task.FromResult(Snapshot());
        });
        _context.PropertyChanged += OnContextChanged;
    }

    /// <summary>The current state as a contract.</summary>
    public LandscapeSnapshot Snapshot()
    {
        var site = _locations.ActiveLocation;
        var landscape = _media.Find(site?.LandscapeId);
        return new LandscapeSnapshot(
            _changes.Revision,
            _media.IsAvailable,
            site is null ? null : Truncate(site.Name, 200),
            site is null ? null : _media.Resolve(site.LandscapeId),
            [.. _media.Landscapes.Take(32).Select(l => new LandscapeOption(
                l.Id, Truncate(l.Name, 100), Truncate(l.Credit.Author, 200), Truncate(l.Credit.License, 50), Truncate(l.Credit.Url, 200)))],
            landscape is not null && site!.HorizonMask.Equals(landscape.ToHorizonMask()));
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IObserverContext.LandscapeId) or nameof(IObserverContext.HorizonMask)
            or nameof(IObserverContext.LocationName))
        {
            _changes.Invalidate();
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    public void Dispose()
    {
        _context.PropertyChanged -= OnContextChanged;
        _changes.Dispose();
        _rpc = null;
    }
}
