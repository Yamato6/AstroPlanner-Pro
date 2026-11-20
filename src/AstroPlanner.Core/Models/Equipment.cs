using AstroPlanner.Core.Imaging;

namespace AstroPlanner.Core.Models;

public enum CameraKind
{
    Dslr,
    Mirrorless,
    AstroCamera
}

public enum SensorColor
{
    /// <summary>One-shot colour (Bayer matrix): each pixel sees one colour band.</summary>
    Color,
    Mono
}

public enum OpticKind
{
    CameraLens,
    Refractor,
    Reflector,
    Catadioptric
}

/// <summary>How a mount follows the sky; decides the longest sub-exposure (spec §8.3).</summary>
public enum MountKind
{
    /// <summary>No tracking: stars trail, the NPF rule limits the exposure.</summary>
    FixedTripod,
    StarTracker,
    /// <summary>Motorised alt-azimuth mount: field rotation limits the exposure.</summary>
    AltAz,
    Equatorial,
    EquatorialGuided
}

/// <summary>
/// A camera body (spec §4.5). Noise figures are estimates the user can refine; QE is the peak
/// quantum efficiency of the sensor (Bayer losses are applied by the feasibility model).
/// </summary>
public sealed record Camera
{
    public Camera(
        string id,
        string name,
        CameraKind kind,
        SensorColor color,
        double sensorWidthMm,
        double sensorHeightMm,
        int resolutionWidthPx,
        int resolutionHeightPx,
        double? pixelSizeUm = null,
        double quantumEfficiency = 0.5,
        double readNoiseElectrons = 3,
        double darkCurrentElectronsPerSecond = 0.1,
        int? recommendedIso = null,
        int? recommendedGain = null,
        string notes = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Positive(sensorWidthMm, nameof(sensorWidthMm), 100);
        Positive(sensorHeightMm, nameof(sensorHeightMm), 100);
        if (resolutionWidthPx is < 1 or > 100_000 || resolutionHeightPx is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(resolutionWidthPx), "Resolution must be between 1 and 100000 pixels.");
        }

        if (pixelSizeUm is { } pixel)
        {
            Positive(pixel, nameof(pixelSizeUm), 50);
        }

        if (!(quantumEfficiency > 0 && quantumEfficiency <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(quantumEfficiency), "QE must be in (0, 1].");
        }

        if (!(readNoiseElectrons >= 0 && readNoiseElectrons <= 100) || !(darkCurrentElectronsPerSecond >= 0 && darkCurrentElectronsPerSecond <= 100))
        {
            throw new ArgumentOutOfRangeException(nameof(readNoiseElectrons), "Noise figures must be between 0 and 100 e⁻.");
        }

        Id = id;
        Name = name.Trim();
        Kind = kind;
        Color = color;
        SensorWidthMm = sensorWidthMm;
        SensorHeightMm = sensorHeightMm;
        ResolutionWidthPx = resolutionWidthPx;
        ResolutionHeightPx = resolutionHeightPx;
        PixelSizeUm = pixelSizeUm;
        QuantumEfficiency = quantumEfficiency;
        ReadNoiseElectrons = readNoiseElectrons;
        DarkCurrentElectronsPerSecond = darkCurrentElectronsPerSecond;
        RecommendedIso = recommendedIso;
        RecommendedGain = recommendedGain;
        Notes = notes ?? "";
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public CameraKind Kind { get; init; }
    public SensorColor Color { get; init; }
    public double SensorWidthMm { get; init; }
    public double SensorHeightMm { get; init; }
    public int ResolutionWidthPx { get; init; }
    public int ResolutionHeightPx { get; init; }

    /// <summary>Pixel pitch as entered; null = derived from the sensor width and resolution.</summary>
    public double? PixelSizeUm { get; init; }

    public double QuantumEfficiency { get; init; }
    public double ReadNoiseElectrons { get; init; }
    public double DarkCurrentElectronsPerSecond { get; init; }
    public int? RecommendedIso { get; init; }
    public int? RecommendedGain { get; init; }
    public string Notes { get; init; }

    /// <summary>Pixel pitch in µm (entered, else sensor width / horizontal resolution).</summary>
    public double EffectivePixelSizeUm => PixelSizeUm ?? SensorWidthMm * 1000.0 / ResolutionWidthPx;

    /// <summary>Crop factor against a 36 × 24 mm sensor (diagonal ratio).</summary>
    public double CropFactor => 43.2666 / Math.Sqrt(SensorWidthMm * SensorWidthMm + SensorHeightMm * SensorHeightMm);

    internal static void Positive(double value, string name, double maximum)
    {
        if (!(value > 0 && value <= maximum))
        {
            throw new ArgumentOutOfRangeException(name, $"Must be greater than 0 and at most {maximum}.");
        }
    }
}

/// <summary>A lens or telescope (spec §4.5).</summary>
public sealed record Optic
{
    public Optic(string id, string name, OpticKind kind, double focalLengthMm, double apertureMm, double transmission = 0.9, string notes = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Camera.Positive(focalLengthMm, nameof(focalLengthMm), 20_000);
        Camera.Positive(apertureMm, nameof(apertureMm), 2_000);
        if (focalLengthMm / apertureMm < 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(apertureMm), "The focal ratio must be at least f/0.5.");
        }

        if (!(transmission > 0 && transmission <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(transmission), "Transmission must be in (0, 1].");
        }

        Id = id;
        Name = name.Trim();
        Kind = kind;
        FocalLengthMm = focalLengthMm;
        ApertureMm = apertureMm;
        Transmission = transmission;
        Notes = notes ?? "";
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public OpticKind Kind { get; init; }
    public double FocalLengthMm { get; init; }
    public double ApertureMm { get; init; }
    public double Transmission { get; init; }
    public string Notes { get; init; }
    public double FocalRatio => FocalLengthMm / ApertureMm;
}

/// <summary>A mount or tripod (spec §4.5).</summary>
public sealed record Mount
{
    public Mount(string id, string name, MountKind kind, double? maxUnguidedExposureSeconds = null, double? payloadKg = null, string notes = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (maxUnguidedExposureSeconds is { } seconds)
        {
            Camera.Positive(seconds, nameof(maxUnguidedExposureSeconds), 3600);
        }

        if (payloadKg is { } kg)
        {
            Camera.Positive(kg, nameof(payloadKg), 1000);
        }

        Id = id;
        Name = name.Trim();
        Kind = kind;
        MaxUnguidedExposureSeconds = maxUnguidedExposureSeconds;
        PayloadKg = payloadKg;
        Notes = notes ?? "";
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public MountKind Kind { get; init; }

    /// <summary>Longest sub the user trusts on this mount; null = the default for its kind (ExposureLimits).</summary>
    public double? MaxUnguidedExposureSeconds { get; init; }

    public double? PayloadKg { get; init; }
    public string Notes { get; init; }

    public bool Tracks => Kind != MountKind.FixedTripod;
}

/// <summary>
/// A rig = camera + optic + mount (+ reducer/barlow as a focal multiplier), referenced by id
/// (spec §4.5). Resolved against the inventory into a <see cref="RigSetup"/>.
/// </summary>
public sealed record Rig
{
    public Rig(string id, string name, string cameraId, string opticId, string mountId, double focalMultiplier = 1.0, string notes = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraId);
        ArgumentException.ThrowIfNullOrWhiteSpace(opticId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mountId);
        if (!(focalMultiplier >= 0.2 && focalMultiplier <= 5))
        {
            throw new ArgumentOutOfRangeException(nameof(focalMultiplier), "The reducer/barlow factor must be between 0.2 and 5.");
        }

        Id = id;
        Name = name.Trim();
        CameraId = cameraId;
        OpticId = opticId;
        MountId = mountId;
        FocalMultiplier = focalMultiplier;
        Notes = notes ?? "";
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public string CameraId { get; init; }
    public string OpticId { get; init; }
    public string MountId { get; init; }

    /// <summary>Reducer (&lt; 1) or barlow (&gt; 1) factor; 1 = none.</summary>
    public double FocalMultiplier { get; init; }

    public string Notes { get; init; }
}

/// <summary>A rig with its parts resolved, and the derived optics of spec §4.5.</summary>
public sealed record RigSetup(Rig Rig, Camera Camera, Optic Optic, Mount Mount)
{
    public string Name => Rig.Name;
    public double FocalLengthMm => Optic.FocalLengthMm * Rig.FocalMultiplier;
    public double ApertureMm => Optic.ApertureMm;
    public double FocalRatio => FocalLengthMm / ApertureMm;
    public double PixelSizeUm => Camera.EffectivePixelSizeUm;
    public FieldOfView FieldOfView => OpticsMath.FieldOfView(Camera.SensorWidthMm, Camera.SensorHeightMm, FocalLengthMm);
    public double ImageScaleArcsecPerPixel => OpticsMath.ImageScaleArcsecPerPixel(PixelSizeUm, FocalLengthMm);

    /// <summary>Resolves a rig against the inventory, or null if a part is missing.</summary>
    public static RigSetup? Resolve(Rig rig, IEnumerable<Camera> cameras, IEnumerable<Optic> optics, IEnumerable<Mount> mounts)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var camera = cameras.FirstOrDefault(c => c.Id == rig.CameraId);
        var optic = optics.FirstOrDefault(o => o.Id == rig.OpticId);
        var mount = mounts.FirstOrDefault(m => m.Id == rig.MountId);
        return camera is null || optic is null || mount is null ? null : new RigSetup(rig, camera, optic, mount);
    }
}

/// <summary>The whole equipment inventory and which rig is active.</summary>
public sealed record EquipmentInventory(
    IReadOnlyList<Camera> Cameras,
    IReadOnlyList<Optic> Optics,
    IReadOnlyList<Mount> Mounts,
    IReadOnlyList<Rig> Rigs,
    string? ActiveRigId)
{
    public static EquipmentInventory Empty { get; } = new([], [], [], [], null);

    public RigSetup? Resolve(string? rigId) =>
        Rigs.FirstOrDefault(r => r.Id == rigId) is { } rig ? RigSetup.Resolve(rig, Cameras, Optics, Mounts) : null;

    public RigSetup? ActiveRig => Resolve(ActiveRigId);

    /// <summary>
    /// The seed demo profile of spec §1.3 (the owner's gear), editable like any other entry:
    /// a Canon EOS Rebel T7 with a Rokinon 16 mm f/2.0 on a fixed tripod (active rig) and the
    /// Celestron StarSense Explorer 130AZ (650 mm f/5) on its manual alt-az mount. Noise figures
    /// are typical published estimates.
    /// </summary>
    public static EquipmentInventory Demo { get; } = new(
        [
            new Camera("canon-t7", "Canon EOS Rebel T7 / 2000D", CameraKind.Dslr, SensorColor.Color, 22.3, 14.9, 6000, 4000,
                pixelSizeUm: 3.72, quantumEfficiency: 0.45, readNoiseElectrons: 3.5, darkCurrentElectronsPerSecond: 0.1,
                recommendedIso: 800, notes: "Noise figures are typical estimates at ISO 800; replace them with measured values.")
        ],
        [
            new Optic("rokinon-16", "Rokinon 16 mm f/2.0 ED AS UMC CS", OpticKind.CameraLens, 16, 8, 0.85),
            new Optic("starsense-130", "Celestron StarSense Explorer 130AZ", OpticKind.Reflector, 650, 130, 0.8)
        ],
        [
            new Mount("fixed-tripod", "Fixed photo tripod", MountKind.FixedTripod),
            new Mount("starsense-az", "StarSense manual alt-az (no tracking)", MountKind.FixedTripod,
                notes: "Manual mount without motors: no tracking, like a fixed tripod.")
        ],
        [
            new Rig("t7-rokinon-16", "T7 + Rokinon 16 mm (tripod)", "canon-t7", "rokinon-16", "fixed-tripod"),
            new Rig("t7-starsense-130", "T7 + StarSense 130 (manual alt-az)", "canon-t7", "starsense-130", "starsense-az")
        ],
        "t7-rokinon-16");
}
