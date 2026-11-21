using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>A camera sensor size offered in the field-of-view settings.</summary>
public sealed record SensorPreset(string Name, double WidthMm, double HeightMm)
{
    public override string ToString() => Name;
}

/// <summary>
/// The camera frame drawn on the sky at its true size (spec §4.2): sensor and focal length (from
/// the active rig, or typed), rotation and centre. The user can drag and rotate it in the renderer;
/// a new selection moves it there.
/// </summary>
public sealed partial class SkyCameraFrameViewModel : ObservableObject
{
    private readonly SkySession _session;
    private readonly EquipmentState? _equipment;
    private readonly Func<Vector3d?> _selectedDirection;
    private readonly bool _initialized;
    private bool _applyingRendererFov;

    /// <param name="selectedDirection">J2000 direction of the selected object at the sky time, or null.</param>
    internal SkyCameraFrameViewModel(SkySession session, EquipmentState? equipment, Func<Vector3d?> selectedDirection)
    {
        _session = session;
        _equipment = equipment;
        _selectedDirection = selectedDirection;
        var settings = session.Settings;
        SensorWidthMm = settings.SensorWidthMm;
        SensorHeightMm = settings.SensorHeightMm;
        FocalLengthMm = settings.FocalLengthMm;
        FovRotationDegrees = settings.FovRotationDegrees;
        IsFovVisible = settings.FovVisible;
        SelectedSensorPreset = SensorPresets.FirstOrDefault(p => p.WidthMm == SensorWidthMm && p.HeightMm == SensorHeightMm);
        _initialized = true;

        if (_equipment is not null)
        {
            _equipment.Changed += (_, _) => ApplyActiveRig();
            ApplyActiveRig();
        }
    }

    public IReadOnlyList<SensorPreset> SensorPresets { get; } =
    [
        new(Strings.SensorFullFrame, 36.0, 24.0),
        new(Strings.SensorApsCNikonSony, 23.5, 15.6),
        new(Strings.SensorApsCCanon, 22.3, 14.9),
        new(Strings.SensorMicroFourThirds, 17.3, 13.0),
        new(Strings.SensorOneInch, 13.2, 8.8),
        new(Strings.SensorImx571, 23.5, 15.7),
        new(Strings.SensorImx533, 11.3, 11.3),
        new(Strings.SensorImx585, 11.1, 6.3)
    ];

    /// <summary>Name of the rig the camera frame comes from; null when it is typed by hand.</summary>
    [ObservableProperty]
    public partial string? FovRigName { get; private set; }

    [ObservableProperty]
    public partial bool IsFovVisible { get; set; }

    partial void OnIsFovVisibleChanged(bool value) => OnFovSettingsChanged();

    [ObservableProperty]
    public partial SensorPreset? SelectedSensorPreset { get; set; }

    partial void OnSelectedSensorPresetChanged(SensorPreset? value)
    {
        if (value is not null)
        {
            SensorWidthMm = value.WidthMm;
            SensorHeightMm = value.HeightMm;
        }
    }

    [ObservableProperty]
    public partial double SensorWidthMm { get; set; }

    partial void OnSensorWidthMmChanged(double value) => OnFovSettingsChanged();

    [ObservableProperty]
    public partial double SensorHeightMm { get; set; }

    partial void OnSensorHeightMmChanged(double value) => OnFovSettingsChanged();

    [ObservableProperty]
    public partial double FocalLengthMm { get; set; }

    partial void OnFocalLengthMmChanged(double value) => OnFovSettingsChanged();

    /// <summary>Position angle of the image's top, from north through east (0 = north up).</summary>
    [ObservableProperty]
    public partial double FovRotationDegrees { get; set; }

    partial void OnFovRotationDegreesChanged(double value) => OnFovSettingsChanged();

    /// <summary>Field of the camera, or null while the sizes typed are not valid.</summary>
    public FieldOfView? CameraFov =>
        SensorWidthMm is > 0 and <= 100 && SensorHeightMm is > 0 and <= 100 && FocalLengthMm is >= 1 and <= 20000
            ? OpticsMath.FieldOfView(SensorWidthMm, SensorHeightMm, FocalLengthMm)
            : null;

    /// <summary>"1.96° × 1.31°" (arcminutes below one degree), or a hint when the sizes are invalid.</summary>
    public string FovText => CameraFov is { } fov ? FormatFov(fov) : Strings.SkyFovInvalid;

    /// <summary>Center of the camera field (J2000), e.g. "RA 05h 35.3m · Dec −05° 23′".</summary>
    public string? FovCenterText => IsFovVisible && Center is { } center ? FormatCenter(SkyFrame.ToEquatorial(center)) : null;

    /// <summary>Center of the frame (J2000 direction); null until there is a selection or a drag.</summary>
    internal Vector3d? Center { get; private set; }

    /// <summary>Puts the frame on the selection when it has no centre yet.</summary>
    internal void CenterOnSelectionIfUnset() => Center ??= _selectedDirection();

    /// <summary>A new selection moves the frame onto it.</summary>
    internal void MoveTo(Vector3d direction)
    {
        Center = direction;
        if (IsFovVisible)
        {
            Send();
        }
    }

    /// <summary>Sends the frame (or hides it) to the renderer.</summary>
    internal void Send()
    {
        if (!_session.IsReady)
        {
            return;
        }

        if (!IsFovVisible || CameraFov is not { } fov)
        {
            _session.Send(SkyMessageCodec.SetFov(new FovPayload(false, 1, 1, 0, null, "")));
            return;
        }

        var rotation = ((FovRotationDegrees % 360) + 360) % 360;
        var label = DisplayNames.Format(Strings.SkyFovLabelFormat, FocalLengthMm, FormatFov(fov));
        _session.Send(SkyMessageCodec.SetFov(new FovPayload(true, fov.WidthDegrees, fov.HeightDegrees, rotation >= 360 ? 0 : rotation,
            Center is { } center ? SkyBridgeMapper.ToVector(center) : null, label)));
    }

    /// <summary>The user dragged or rotated the frame in the renderer: remember it without echoing it back.</summary>
    internal void OnRendererFovChanged(FovChangedPayload fov)
    {
        Center = new Vector3d(fov.X, fov.Y, fov.Z);
        _applyingRendererFov = true;
        try
        {
            FovRotationDegrees = Math.Round(fov.RotationDegrees, 1);
        }
        finally
        {
            _applyingRendererFov = false;
        }

        OnPropertyChanged(nameof(FovCenterText));
    }

    /// <summary>The camera frame follows the active rig (sensor and effective focal length); without one it stays manual.</summary>
    private void ApplyActiveRig()
    {
        if (_equipment?.ActiveRig is not { } rig)
        {
            FovRigName = null;
            return;
        }

        SensorWidthMm = rig.Camera.SensorWidthMm;
        SensorHeightMm = rig.Camera.SensorHeightMm;
        FocalLengthMm = rig.FocalLengthMm;
        SelectedSensorPreset = SensorPresets.FirstOrDefault(p => p.WidthMm == SensorWidthMm && p.HeightMm == SensorHeightMm);
        FovRigName = rig.Name;
    }

    private void OnFovSettingsChanged()
    {
        if (!_initialized)
        {
            return;
        }

        if (SelectedSensorPreset is { } preset && (preset.WidthMm != SensorWidthMm || preset.HeightMm != SensorHeightMm))
        {
            SelectedSensorPreset = null;
        }

        var settings = _session.Settings;
        settings.FovVisible = IsFovVisible;
        settings.SensorWidthMm = SensorWidthMm;
        settings.SensorHeightMm = SensorHeightMm;
        settings.FocalLengthMm = FocalLengthMm;
        settings.FovRotationDegrees = FovRotationDegrees;
        _session.SaveSettings();
        OnPropertyChanged(nameof(CameraFov));
        OnPropertyChanged(nameof(FovText));
        OnPropertyChanged(nameof(FovCenterText));
        if (!_applyingRendererFov)
        {
            CenterOnSelectionIfUnset();
            Send();
        }
    }

    internal static string FormatFov(FieldOfView fov) =>
        Math.Max(fov.WidthDegrees, fov.HeightDegrees) < 1
            ? DisplayNames.Format(Strings.SkyFovArcminFormat, fov.WidthDegrees * 60, fov.HeightDegrees * 60)
            : DisplayNames.Format(Strings.SkyFovFormat, fov.WidthDegrees, fov.HeightDegrees);

    private static string FormatCenter(EquatorialCoordinates c)
    {
        var raMinutes = Math.Round(c.RightAscensionHours * 60, 1);
        var decMinutes = (int)Math.Round(Math.Abs(c.DeclinationDegrees) * 60);
        return DisplayNames.Format(Strings.SkyFovCenterFormat,
            (int)(raMinutes / 60) % 24, raMinutes % 60, c.DeclinationDegrees < 0 ? "−" : "+", decMinutes / 60, decMinutes % 60);
    }
}
