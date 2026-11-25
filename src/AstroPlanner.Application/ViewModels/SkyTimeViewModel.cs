using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.Sky;

namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// The sky clock: play at ×1…×3600, pause, step and back to now. While playing only the renderer
/// animates (re-synced every <see cref="ResyncInterval"/>); pausing or stepping applies the sky time
/// to the observer context, so Targets recalculates once.
/// </summary>
public sealed partial class SkyTimeViewModel : ObservableObject, IDisposable
{
    /// <summary>How often the renderer clock is re-synced while playing (real time).</summary>
    internal static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(5);

    /// <summary>The observer time counts as "now" (sky runs at ×1) when it is this close to the clock.</summary>
    private static readonly TimeSpan LiveTolerance = TimeSpan.FromSeconds(90);

    private readonly SkySession _session;
    private readonly SkyBodyFeed _feed;
    private ITimer? _resyncTimer;
    private SynchronizationContext? _uiContext;

    internal SkyTimeViewModel(SkySession session, SkyBodyFeed feed)
    {
        _session = session;
        _feed = feed;
        _session.Context.PropertyChanged += OnContextPropertyChanged;
        SyncToContext();
    }

    /// <summary>Raised after each re-sync while playing (the sky time moved).</summary>
    public event EventHandler? Resynced;

    public IReadOnlyList<double> Rates { get; } = [1, 60, 600, 3600];

    [ObservableProperty]
    public partial double SelectedRate { get; set; } = 60;

    partial void OnSelectedRateChanged(double value)
    {
        if (IsPlaying)
        {
            Clock.Set(Clock.UtcNow, value);
            _feed.SendTime();
        }
    }

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    /// <summary>Sky time in the observer's time zone, for display.</summary>
    public string SkyTimeText =>
        TimeZoneInfo.ConvertTimeFromUtc(Clock.UtcNow, _session.Context.TimeZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    internal SkyClock Clock => _session.Clock;

    [RelayCommand]
    private void Play()
    {
        if (IsPlaying)
        {
            return;
        }

        IsPlaying = true;
        Clock.Set(_session.Context.ToUtc(_session.Context.ObservationDateTime), SelectedRate);
        _feed.SendTime();
        _uiContext = SynchronizationContext.Current;
        _resyncTimer = _session.Time.CreateTimer(_ => OnResyncTimer(), null, ResyncInterval, ResyncInterval);
    }

    [RelayCommand]
    private void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        StopTimer();
        var utc = Clock.UtcNow;
        Clock.Set(utc, 0);
        _feed.SendTime();
        ApplyToContext(utc);
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Moves the sky (and the observation time) by the given minutes ("1", "-10", "1440"…).</summary>
    [RelayCommand]
    private void Step(string minutes)
    {
        if (!int.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta))
        {
            return;
        }

        Pause();
        ApplyToContext(_session.Context.ToUtc(_session.Context.ObservationDateTime).AddMinutes(delta));
    }

    /// <summary>Shows the sky at this UTC instant (and pauses): the planetarium's darkness bar and date field.</summary>
    public void SetTime(DateTime utc)
    {
        Pause();
        ApplyToContext(utc);
    }

    [RelayCommand]
    private void Now()
    {
        Pause();
        _session.Context.ObservationDateTime = _session.Context.Now;
    }

    /// <summary>While playing: re-syncs the renderer clock and refreshes the frame and tracks when needed.</summary>
    internal void Resync()
    {
        if (!IsPlaying)
        {
            return;
        }

        _feed.RefreshWhilePlaying();
        OnPropertyChanged(nameof(SkyTimeText));
        Resynced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>When paused, the sky shows the observer context time (running at ×1 if that is "now").</summary>
    internal void SyncToContext()
    {
        var context = _session.Context;
        var utc = context.ToUtc(context.ObservationDateTime);
        var isNow = (utc - context.ToUtc(context.Now)).Duration() <= LiveTolerance;
        Clock.Set(utc, isNow ? 1 : 0);
        OnPropertyChanged(nameof(SkyTimeText));
    }

    private void ApplyToContext(DateTime utc)
    {
        var context = _session.Context;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), context.TimeZone);
        context.ObservationDateTime = DateTime.SpecifyKind(
            new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Unspecified);
    }

    private void OnResyncTimer()
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            Resync();
        }
        else
        {
            _uiContext.Post(_ => Resync(), null);
        }
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IObserverContext.ObservationDateTime) && IsPlaying)
        {
            // The user picked a time in the top bar: stop the animation there.
            StopTimer();
        }
    }

    private void StopTimer()
    {
        IsPlaying = false;
        _resyncTimer?.Dispose();
        _resyncTimer = null;
    }

    public void Dispose()
    {
        _resyncTimer?.Dispose();
        _resyncTimer = null;
    }
}
