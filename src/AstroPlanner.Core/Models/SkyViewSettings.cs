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
    public bool Labels { get; set; } = true;
    public bool Ground { get; set; } = true;

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
