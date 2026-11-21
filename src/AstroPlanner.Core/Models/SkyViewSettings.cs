namespace AstroPlanner.Core.Models;

/// <summary>Persisted state of the sky view: visible layers, limits and the last camera.</summary>
public sealed class SkyViewSettings
{
    public bool Stars { get; set; } = true;
    public bool ConstellationLines { get; set; } = true;
    public bool ConstellationBoundaries { get; set; }
    public bool ConstellationNames { get; set; } = true;
    public bool AltAzGrid { get; set; }
    public bool EquatorialGrid { get; set; }
    public bool Ecliptic { get; set; }
    public bool Meridian { get; set; }
    public bool MilkyWay { get; set; } = true;
    public bool DeepSky { get; set; } = true;
    public bool Bodies { get; set; } = true;
    public bool Satellites { get; set; } = true;
    public bool Labels { get; set; } = true;
    public bool Ground { get; set; } = true;

    /// <summary>Tint the sky by twilight, moonlight and the site's light pollution.</summary>
    public bool SkyBrightness { get; set; } = true;

    /// <summary>"dome", "allSky" or "finder" (the protocol names).</summary>
    public string ViewMode { get; set; } = "dome";

    /// <summary>Finder chart field, degrees.</summary>
    public double FinderFovDegrees { get; set; } = 10;

    /// <summary>Camera field-of-view overlay, set by hand until rigs arrive (M3).</summary>
    public bool FovVisible { get; set; }

    /// <summary>Default: the owner's Canon APS-C body (spec §1.3) on the 650 mm telescope.</summary>
    public double SensorWidthMm { get; set; } = 22.3;

    public double SensorHeightMm { get; set; } = 14.9;

    public double FocalLengthMm { get; set; } = 650;

    /// <summary>Position angle of the image's top, from north through east.</summary>
    public double FovRotationDegrees { get; set; }

    /// <summary>Faintest star drawn at a 60° field (spec §4.2 default: 8.5).</summary>
    public double StarLimitMagnitude { get; set; } = 8.5;

    /// <summary>Faintest deep-sky object drawn at a 60° field.</summary>
    public double DeepSkyLimitMagnitude { get; set; } = 10;

    public double AzimuthDegrees { get; set; } = 180;
    public double AltitudeDegrees { get; set; } = 35;
    public double FovDegrees { get; set; } = 70;

    /// <summary>Frame-rate overlay in the renderer.</summary>
    public bool ShowStats { get; set; }

    public SkyViewSettings Clone() => (SkyViewSettings)MemberwiseClone();
}
