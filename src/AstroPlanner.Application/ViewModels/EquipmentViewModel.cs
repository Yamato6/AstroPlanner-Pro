using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.Services;

namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// Equipment inventory (spec §4.5): cameras, optics, mounts and rigs, each edited in a form and
/// saved at once; the derived optics of the rig being edited (field, image scale, sampling, NPF,
/// suggested sub) are shown live.
/// </summary>
public sealed partial class EquipmentViewModel : ObservableObject
{
    /// <summary>Seeing assumed for the sampling hint, arcseconds.</summary>
    public const double TypicalSeeingArcsec = 2.5;

    private readonly EquipmentState _state;

    public EquipmentViewModel(EquipmentState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.Changed += (_, _) => Refresh();
        Refresh();

        // Open on something to edit: the active rig and the first of each part.
        SelectedRig = Rigs.FirstOrDefault(r => r.Rig.Id == ActiveRigId) ?? FirstOf(Rigs);
        SelectedCamera = FirstOf(Cameras);
        SelectedOptic = FirstOf(Optics);
        SelectedMount = FirstOf(Mounts);
    }

    public EquipmentInventory Inventory => _state.Inventory;
    public IReadOnlyList<Camera> Cameras => _state.Inventory.Cameras;
    public IReadOnlyList<Optic> Optics => _state.Inventory.Optics;
    public IReadOnlyList<Mount> Mounts => _state.Inventory.Mounts;
    public IReadOnlyList<RigSetup> Rigs => _state.Rigs;

    public IReadOnlyList<FilterOption> CameraKinds { get; } = Options<CameraKind>(k => k switch
    {
        CameraKind.Dslr => Strings.CameraKindDslr,
        CameraKind.Mirrorless => Strings.CameraKindMirrorless,
        _ => Strings.CameraKindAstro
    });

    public IReadOnlyList<FilterOption> SensorColors { get; } = Options<SensorColor>(c =>
        c == SensorColor.Color ? Strings.SensorColorColor : Strings.SensorColorMono);

    public IReadOnlyList<FilterOption> OpticKinds { get; } = Options<OpticKind>(k => k switch
    {
        OpticKind.CameraLens => Strings.OpticKindLens,
        OpticKind.Refractor => Strings.OpticKindRefractor,
        OpticKind.Reflector => Strings.OpticKindReflector,
        _ => Strings.OpticKindCatadioptric
    });

    public IReadOnlyList<FilterOption> MountKinds { get; } = Options<MountKind>(k => k switch
    {
        MountKind.FixedTripod => Strings.MountKindTripod,
        MountKind.StarTracker => Strings.MountKindTracker,
        MountKind.AltAz => Strings.MountKindAltAz,
        MountKind.Equatorial => Strings.MountKindEquatorial,
        _ => Strings.MountKindGuided
    });

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Inventory));
        OnPropertyChanged(nameof(Cameras));
        OnPropertyChanged(nameof(Optics));
        OnPropertyChanged(nameof(Mounts));
        OnPropertyChanged(nameof(Rigs));
        OnPropertyChanged(nameof(ActiveRigId));
    }

    private bool Commit(EquipmentInventory inventory, string savedMessage)
    {
        if (!_state.Save(inventory))
        {
            Fail(Strings.EquipmentSaveFailed);
            return false;
        }

        HasError = false;
        StatusMessage = savedMessage;
        return true;
    }

    private void Fail(string message)
    {
        HasError = true;
        StatusMessage = message;
    }

    private static T? FirstOf<T>(IReadOnlyList<T> items) where T : class => items.Count > 0 ? items[0] : null;

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    // ---- active rig ---------------------------------------------------------------------------------

    /// <summary>Id of the active rig (bound to the selector).</summary>
    public string? ActiveRigId
    {
        get => _state.Inventory.ActiveRigId;
        set
        {
            if (value != _state.Inventory.ActiveRigId)
            {
                _state.SetActiveRig(value);
                OnPropertyChanged();
            }
        }
    }

    // ---- cameras ------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial Camera? SelectedCamera { get; set; }

    [ObservableProperty] public partial string CameraName { get; set; } = "";
    [ObservableProperty] public partial string CameraKindValue { get; set; } = nameof(CameraKind.Dslr);
    [ObservableProperty] public partial string CameraColorValue { get; set; } = nameof(SensorColor.Color);
    [ObservableProperty] public partial string SensorWidthText { get; set; } = "";
    [ObservableProperty] public partial string SensorHeightText { get; set; } = "";
    [ObservableProperty] public partial string ResolutionWidthText { get; set; } = "";
    [ObservableProperty] public partial string ResolutionHeightText { get; set; } = "";
    [ObservableProperty] public partial string PixelSizeText { get; set; } = "";
    [ObservableProperty] public partial string QuantumEfficiencyText { get; set; } = "";
    [ObservableProperty] public partial string ReadNoiseText { get; set; } = "";
    [ObservableProperty] public partial string DarkCurrentText { get; set; } = "";
    [ObservableProperty] public partial string IsoText { get; set; } = "";
    [ObservableProperty] public partial string GainText { get; set; } = "";
    [ObservableProperty] public partial string CameraNotes { get; set; } = "";

    partial void OnSelectedCameraChanged(Camera? value)
    {
        var c = value ?? new Camera("new", Strings.EquipmentNewCamera, CameraKind.Dslr, SensorColor.Color, 22.3, 14.9, 6000, 4000);
        CameraName = value?.Name ?? "";
        CameraKindValue = c.Kind.ToString();
        CameraColorValue = c.Color.ToString();
        SensorWidthText = Text(c.SensorWidthMm);
        SensorHeightText = Text(c.SensorHeightMm);
        ResolutionWidthText = Text(c.ResolutionWidthPx);
        ResolutionHeightText = Text(c.ResolutionHeightPx);
        PixelSizeText = Text(c.PixelSizeUm);
        QuantumEfficiencyText = Text(c.QuantumEfficiency);
        ReadNoiseText = Text(c.ReadNoiseElectrons);
        DarkCurrentText = Text(c.DarkCurrentElectronsPerSecond);
        IsoText = Text(c.RecommendedIso);
        GainText = Text(c.RecommendedGain);
        CameraNotes = c.Notes;
    }

    [RelayCommand]
    private void NewCamera()
    {
        SelectedCamera = null;
        OnSelectedCameraChanged(null);
    }

    [RelayCommand]
    private void SaveCamera()
    {
        try
        {
            var id = SelectedCamera?.Id ?? NewId();
            var camera = new Camera(id, Required(CameraName), Enum.Parse<CameraKind>(CameraKindValue), Enum.Parse<SensorColor>(CameraColorValue),
                Number(SensorWidthText, Strings.FieldSensorWidth), Number(SensorHeightText, Strings.FieldSensorHeight),
                (int)Number(ResolutionWidthText, Strings.FieldResolution), (int)Number(ResolutionHeightText, Strings.FieldResolution),
                Optional(PixelSizeText, Strings.FieldPixelSize), Number(QuantumEfficiencyText, Strings.FieldQe),
                Number(ReadNoiseText, Strings.FieldReadNoise), Number(DarkCurrentText, Strings.FieldDarkCurrent),
                (int?)Optional(IsoText, Strings.FieldIso), (int?)Optional(GainText, Strings.FieldGain), CameraNotes);
            if (Commit(Inventory with { Cameras = Upsert(Cameras, camera, c => c.Id) }, Strings.EquipmentSaved))
            {
                SelectedCamera = Cameras.FirstOrDefault(c => c.Id == id);
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Fail(ex is ArgumentOutOfRangeException range ? Strings.EquipmentInvalidValue + " " + range.ParamName : ex.Message);
        }
    }

    [RelayCommand]
    private void DeleteCamera()
    {
        if (SelectedCamera is not { } camera)
        {
            return;
        }

        if (Inventory.Rigs.Any(r => r.CameraId == camera.Id))
        {
            Fail(Strings.EquipmentInUse);
            return;
        }

        Commit(Inventory with { Cameras = [.. Cameras.Where(c => c.Id != camera.Id)] }, Strings.EquipmentDeleted);
        NewCamera();
    }

    // ---- optics -------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial Optic? SelectedOptic { get; set; }

    [ObservableProperty] public partial string OpticName { get; set; } = "";
    [ObservableProperty] public partial string OpticKindValue { get; set; } = nameof(OpticKind.CameraLens);
    [ObservableProperty] public partial string FocalLengthText { get; set; } = "";
    [ObservableProperty] public partial string ApertureText { get; set; } = "";

    /// <summary>Alternative to the aperture: the f-number (used when the aperture is empty).</summary>
    [ObservableProperty] public partial string FocalRatioText { get; set; } = "";

    [ObservableProperty] public partial string TransmissionText { get; set; } = "";
    [ObservableProperty] public partial string OpticNotes { get; set; } = "";

    partial void OnSelectedOpticChanged(Optic? value)
    {
        OpticName = value?.Name ?? "";
        OpticKindValue = (value?.Kind ?? OpticKind.CameraLens).ToString();
        FocalLengthText = Text(value?.FocalLengthMm);
        ApertureText = Text(value?.ApertureMm);
        FocalRatioText = Text(value?.FocalRatio);
        TransmissionText = Text(value?.Transmission ?? 0.9);
        OpticNotes = value?.Notes ?? "";
    }

    [RelayCommand]
    private void NewOptic()
    {
        SelectedOptic = null;
        OnSelectedOpticChanged(null);
    }

    [RelayCommand]
    private void SaveOptic()
    {
        try
        {
            var id = SelectedOptic?.Id ?? NewId();
            var focal = Number(FocalLengthText, Strings.FieldFocalLength);
            var aperture = Optional(ApertureText, Strings.FieldAperture)
                           ?? focal / Number(FocalRatioText, Strings.FieldFocalRatio);
            var optic = new Optic(id, Required(OpticName), Enum.Parse<OpticKind>(OpticKindValue), focal, aperture,
                Number(TransmissionText, Strings.FieldTransmission), OpticNotes);
            if (Commit(Inventory with { Optics = Upsert(Optics, optic, o => o.Id) }, Strings.EquipmentSaved))
            {
                SelectedOptic = Optics.FirstOrDefault(o => o.Id == id);
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Fail(ex is ArgumentOutOfRangeException range ? Strings.EquipmentInvalidValue + " " + range.ParamName : ex.Message);
        }
    }

    [RelayCommand]
    private void DeleteOptic()
    {
        if (SelectedOptic is not { } optic)
        {
            return;
        }

        if (Inventory.Rigs.Any(r => r.OpticId == optic.Id))
        {
            Fail(Strings.EquipmentInUse);
            return;
        }

        Commit(Inventory with { Optics = [.. Optics.Where(o => o.Id != optic.Id)] }, Strings.EquipmentDeleted);
        NewOptic();
    }

    // ---- mounts -------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial Mount? SelectedMount { get; set; }

    [ObservableProperty] public partial string MountName { get; set; } = "";
    [ObservableProperty] public partial string MountKindValue { get; set; } = nameof(MountKind.FixedTripod);
    [ObservableProperty] public partial string MaxExposureText { get; set; } = "";
    [ObservableProperty] public partial string PayloadText { get; set; } = "";
    [ObservableProperty] public partial string MountNotes { get; set; } = "";

    partial void OnSelectedMountChanged(Mount? value)
    {
        MountName = value?.Name ?? "";
        MountKindValue = (value?.Kind ?? MountKind.FixedTripod).ToString();
        MaxExposureText = Text(value?.MaxUnguidedExposureSeconds);
        PayloadText = Text(value?.PayloadKg);
        MountNotes = value?.Notes ?? "";
    }

    [RelayCommand]
    private void NewMount()
    {
        SelectedMount = null;
        OnSelectedMountChanged(null);
    }

    [RelayCommand]
    private void SaveMount()
    {
        try
        {
            var id = SelectedMount?.Id ?? NewId();
            var mount = new Mount(id, Required(MountName), Enum.Parse<MountKind>(MountKindValue),
                Optional(MaxExposureText, Strings.FieldMaxExposure), Optional(PayloadText, Strings.FieldPayload), MountNotes);
            if (Commit(Inventory with { Mounts = Upsert(Mounts, mount, m => m.Id) }, Strings.EquipmentSaved))
            {
                SelectedMount = Mounts.FirstOrDefault(m => m.Id == id);
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Fail(ex is ArgumentOutOfRangeException range ? Strings.EquipmentInvalidValue + " " + range.ParamName : ex.Message);
        }
    }

    [RelayCommand]
    private void DeleteMount()
    {
        if (SelectedMount is not { } mount)
        {
            return;
        }

        if (Inventory.Rigs.Any(r => r.MountId == mount.Id))
        {
            Fail(Strings.EquipmentInUse);
            return;
        }

        Commit(Inventory with { Mounts = [.. Mounts.Where(m => m.Id != mount.Id)] }, Strings.EquipmentDeleted);
        NewMount();
    }

    // ---- rigs ---------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial RigSetup? SelectedRig { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DerivedText))]
    public partial string RigName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DerivedText))]
    public partial Camera? RigCamera { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DerivedText))]
    public partial Optic? RigOptic { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DerivedText))]
    public partial Mount? RigMount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DerivedText))]
    public partial string FocalMultiplierText { get; set; } = "1";

    [ObservableProperty] public partial string RigNotes { get; set; } = "";

    partial void OnSelectedRigChanged(RigSetup? value)
    {
        RigName = value?.Name ?? "";
        RigCamera = value is null ? Cameras.FirstOrDefault() : Cameras.FirstOrDefault(c => c.Id == value.Camera.Id);
        RigOptic = value is null ? Optics.FirstOrDefault() : Optics.FirstOrDefault(o => o.Id == value.Optic.Id);
        RigMount = value is null ? Mounts.FirstOrDefault() : Mounts.FirstOrDefault(m => m.Id == value.Mount.Id);
        FocalMultiplierText = Text(value?.Rig.FocalMultiplier ?? 1);
        RigNotes = value?.Rig.Notes ?? "";
    }

    /// <summary>The rig as currently edited, or null while the form is incomplete or invalid.</summary>
    public RigSetup? PreviewRig
    {
        get
        {
            if (RigCamera is null || RigOptic is null || RigMount is null || !TryParse(FocalMultiplierText, out var multiplier))
            {
                return null;
            }

            try
            {
                var rig = new Rig("preview", string.IsNullOrWhiteSpace(RigName) ? "-" : RigName, RigCamera.Id, RigOptic.Id, RigMount.Id, multiplier);
                return new RigSetup(rig, RigCamera, RigOptic, RigMount);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>Field, scale, sampling, NPF / 500 rule and mount limit of the edited rig (spec §4.5 "derived values").</summary>
    public string DerivedText
    {
        get
        {
            if (PreviewRig is not { } rig)
            {
                return Strings.EquipmentDerivedIncomplete;
            }

            var fov = rig.FieldOfView;
            var sampling = OpticsMath.SamplingFor(TypicalSeeingArcsec, rig.ImageScaleArcsecPerPixel);
            var samplingText = sampling.Verdict switch
            {
                SamplingVerdict.Undersampled => Strings.SamplingUnder,
                SamplingVerdict.Oversampled => Strings.SamplingOver,
                _ => Strings.SamplingGood
            };
            var npf = ExposureLimits.Npf(rig.FocalRatio, rig.PixelSizeUm, rig.FocalLengthMm);
            var limit = rig.Mount.Tracks
                ? DisplayNames.Format(Strings.DerivedMountLimitFormat, FeasibilityText.Seconds(ExposureLimits.MountLimit(rig, 0).Seconds))
                : DisplayNames.Format(Strings.DerivedNpfFormat, FeasibilityText.Seconds(npf),
                    FeasibilityText.Seconds(ExposureLimits.Rule500(rig.FocalLengthMm, rig.Camera.CropFactor)));
            return DisplayNames.Format(Strings.DerivedRigFormat,
                fov.WidthDegrees, fov.HeightDegrees, rig.ImageScaleArcsecPerPixel, rig.FocalLengthMm, rig.FocalRatio,
                samplingText, sampling.FwhmPixels, limit);
        }
    }

    [RelayCommand]
    private void NewRig()
    {
        SelectedRig = null;
        OnSelectedRigChanged(null);
    }

    [RelayCommand]
    private void SaveRig()
    {
        if (RigCamera is null || RigOptic is null || RigMount is null)
        {
            Fail(Strings.EquipmentRigIncomplete);
            return;
        }

        try
        {
            var id = SelectedRig?.Rig.Id ?? NewId();
            var rig = new Rig(id, Required(RigName), RigCamera.Id, RigOptic.Id, RigMount.Id,
                Number(FocalMultiplierText, Strings.FieldFocalMultiplier), RigNotes);
            var inventory = Inventory with { Rigs = Upsert(Inventory.Rigs, rig, r => r.Id) };
            // The first rig becomes the active one.
            inventory = inventory.ActiveRigId is null ? inventory with { ActiveRigId = id } : inventory;
            if (Commit(inventory, Strings.EquipmentSaved))
            {
                SelectedRig = Rigs.FirstOrDefault(r => r.Rig.Id == id);
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Fail(ex is ArgumentOutOfRangeException range ? Strings.EquipmentInvalidValue + " " + range.ParamName : ex.Message);
        }
    }

    [RelayCommand]
    private void DeleteRig()
    {
        if (SelectedRig is not { } setup)
        {
            return;
        }

        var inventory = Inventory with { Rigs = [.. Inventory.Rigs.Where(r => r.Id != setup.Rig.Id)] };
        if (inventory.ActiveRigId == setup.Rig.Id)
        {
            inventory = inventory with { ActiveRigId = inventory.Rigs.Count > 0 ? inventory.Rigs[0].Id : null };
        }

        Commit(inventory, Strings.EquipmentDeleted);
        NewRig();
    }

    [RelayCommand]
    private void ActivateRig()
    {
        if (SelectedRig is { } setup)
        {
            ActiveRigId = setup.Rig.Id;
            HasError = false;
            StatusMessage = DisplayNames.Format(Strings.EquipmentActivatedFormat, setup.Name);
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static IReadOnlyList<FilterOption> Options<T>(Func<T, string> label) where T : struct, Enum =>
        [.. Enum.GetValues<T>().Select(v => new FilterOption(v.ToString(), label(v)))];

    private static List<T> Upsert<T>(IReadOnlyList<T> items, T item, Func<T, string> id)
    {
        var list = items.ToList();
        var index = list.FindIndex(existing => id(existing) == id(item));
        if (index >= 0)
        {
            list[index] = item;
        }
        else
        {
            list.Add(item);
        }

        return list;
    }

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new FormatException(Strings.EquipmentNameRequired) : value.Trim();

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static double Number(string text, string field) =>
        TryParse(text, out var value) ? value : throw new FormatException(DisplayNames.Format(Strings.EquipmentNumberRequiredFormat, field));

    private static double? Optional(string text, string field) =>
        string.IsNullOrWhiteSpace(text) ? null : Number(text, field);

    private static string Text(double? value) => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";

    private static string Text(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "";
}
