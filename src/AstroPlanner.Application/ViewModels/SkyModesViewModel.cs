using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging;
using AstroPlanner.SkyBridge;

namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// The renderer's view modes (spec §4.2): the 3D dome, the all-sky chart and the finder chart,
/// which is centred on the selection (else on the camera frame) with a field three times the
/// camera's. Also names the PNG the view is exported to.
/// </summary>
public sealed partial class SkyModesViewModel : ObservableObject
{
    private readonly SkySession _session;
    private readonly Func<FieldOfView?> _visibleCameraField;
    private readonly Func<Vector3d?> _finderCenter;
    private readonly Func<string?> _selectedId;

    /// <param name="visibleCameraField">The camera's field while its frame is shown, else null.</param>
    /// <param name="finderCenter">Where the finder chart goes: the selection, else the camera frame.</param>
    /// <param name="selectedId">Id of the selected catalog object, or null.</param>
    internal SkyModesViewModel(SkySession session, Func<FieldOfView?> visibleCameraField, Func<Vector3d?> finderCenter, Func<string?> selectedId)
    {
        _session = session;
        _visibleCameraField = visibleCameraField;
        _finderCenter = finderCenter;
        _selectedId = selectedId;
        var saved = session.Settings.ViewMode;
        ViewMode = saved is SkyViewModes.AllSky or SkyViewModes.Finder ? saved : SkyViewModes.Dome;
    }

    /// <summary>Raised when an action elsewhere (Detail, Targets) wants the Sky tab shown.</summary>
    public event EventHandler? ShowSkyRequested;

    /// <summary>Current renderer mode, as a protocol name (<see cref="SkyViewModes"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDomeMode), nameof(IsAllSkyMode), nameof(IsFinderMode))]
    public partial string ViewMode { get; private set; } = SkyViewModes.Dome;

    public bool IsDomeMode
    {
        get => ViewMode == SkyViewModes.Dome;
        set { if (value) SetViewMode(SkyViewModes.Dome); }
    }

    public bool IsAllSkyMode
    {
        get => ViewMode == SkyViewModes.AllSky;
        set { if (value) SetViewMode(SkyViewModes.AllSky); }
    }

    public bool IsFinderMode
    {
        get => ViewMode == SkyViewModes.Finder;
        set { if (value) SetViewMode(SkyViewModes.Finder); }
    }

    /// <summary>
    /// Field of the finder chart: three times the camera field when the overlay is on (so that
    /// the frame and its surroundings show), else the remembered field.
    /// </summary>
    public double FinderFovDegrees => _visibleCameraField() is { } fov
        ? Math.Clamp(3 * Math.Max(fov.WidthDegrees, fov.HeightDegrees), 1, 60)
        : Math.Clamp(_session.Settings.FinderFovDegrees, 1, 60);

    internal void SetViewMode(string mode)
    {
        if (mode == ViewMode)
        {
            return;
        }

        ViewMode = mode;
        _session.Settings.ViewMode = mode;
        _session.SaveSettings();
        _session.Send(SkyMessageCodec.SetViewMode(new ViewModePayload(mode)));
        if (mode == SkyViewModes.Finder)
        {
            CenterFinder();
        }
    }

    /// <summary>The finder chart is centred on the selected object, else on the camera field.</summary>
    internal void CenterFinder()
    {
        if (_finderCenter() is { } center)
        {
            var v = SkyBridgeMapper.ToVector(center);
            _session.Send(SkyMessageCodec.CenterOn(new CenterOnPayload(v.X, v.Y, v.Z, FinderFovDegrees)));
        }
    }

    /// <summary>Finder chart of the selected object ("Open finder chart" in Detail and Targets).</summary>
    [RelayCommand]
    private void OpenFinderChart()
    {
        ShowSkyRequested?.Invoke(this, EventArgs.Empty);
        if (IsFinderMode)
        {
            CenterFinder();
        }
        else
        {
            SetViewMode(SkyViewModes.Finder);
        }
    }

    /// <summary>File name offered when the view is exported as PNG.</summary>
    public string SuggestedImageFileName()
    {
        var subject = IsFinderMode && _selectedId() is { } id
            ? "-" + string.Concat(id.Split(System.IO.Path.GetInvalidFileNameChars().Append(' ').ToArray()))
            : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"AstroPlannerPro-{ViewMode}{subject}-{TimeZoneInfo.ConvertTimeFromUtc(_session.Clock.UtcNow, _session.Context.TimeZone):yyyy-MM-dd-HHmm}.png");
    }
}
