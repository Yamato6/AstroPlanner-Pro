using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.Sky;

namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// What the parts of the sky tab share: the observer context, the renderer channel (messages are
/// dropped until the renderer reports <c>ready</c>), the persisted view settings and the sky clock.
/// Owned by <see cref="SkyViewModel"/>; the parts never reference each other.
/// </summary>
internal sealed class SkySession
{
    private readonly ISkyViewStore _store;

    public SkySession(IObserverContext context, ISkyViewStore store, IAppLogger logger, TimeProvider time)
    {
        Context = context;
        _store = store;
        Logger = logger;
        Time = time;
        Clock = new SkyClock(time);
        Settings = store.Load();
    }

    public IObserverContext Context { get; }

    public IAppLogger Logger { get; }

    public TimeProvider Time { get; }

    /// <summary>The time shown by the sky: paused, or running at a rate while playing.</summary>
    public SkyClock Clock { get; }

    /// <summary>Layers, camera, view and frame settings; saved with <see cref="SaveSettings"/>.</summary>
    public SkyViewSettings Settings { get; }

    public ISkyChannel? Channel { get; set; }

    /// <summary>The renderer reported <c>ready</c>; until then nothing is sent.</summary>
    public bool IsReady { get; set; }

    /// <summary>Posts a message to the renderer, if it is ready.</summary>
    public void Send(string json)
    {
        if (IsReady)
        {
            Channel?.Post(json);
        }
    }

    public void SaveSettings()
    {
        try
        {
            _store.Save(Settings.Clone());
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Data.Common.DbException or InvalidOperationException or UnauthorizedAccessException)
        {
            Logger.LogError("Could not save the sky view settings.", ex);
        }
    }
}
