using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Planning;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// A catalog object with its position at the chosen time and its ephemeris for the night. One
/// instance per object, shared by the targets table, the plan, the sky chart and the detail.
/// </summary>
public sealed partial class CelestialObjectRowViewModel : ObservableObject
{
    public CelestialObjectRowViewModel(CelestialObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        ApparentMagnitude = target.ApparentMagnitude;
        Constellation = target.Constellation;
        Search = CatalogSearch.Index(target);
        CrossIdentifiers = string.Join(", ", target.Designations.Where(IsMainCatalog).Take(3));
        SizeText = DisplayNames.Size(target.MajorAxisArcmin, target.MinorAxisArcmin);
    }

    /// <summary>M, NGC, IC or Caldwell: the identifiers worth showing in the table.</summary>
    private static bool IsMainCatalog(string designation) =>
        designation.StartsWith("NGC ", StringComparison.Ordinal)
        || designation.StartsWith("IC ", StringComparison.Ordinal)
        || (designation.Length > 1 && designation[0] is 'M' or 'C' && designation.Skip(1).All(char.IsAsciiDigit));

    public CelestialObject Target { get; }
    public string Id => Target.Id;

    /// <summary>The common name, or the id when the object has none (for exports and the logbook).</summary>
    public string CommonName => Target.DisplayName;

    /// <summary>The common name only; empty when the object has none (the table already shows the id).</summary>
    public string Name => Target.CommonName;

    /// <summary>"M42 — Orion Nebula", or just the id for an object without a name.</summary>
    public string Title => Name.Length > 0 ? $"{Id} — {Name}" : Id;
    public CelestialObjectType Type => Target.Type;
    public EquatorialCoordinates Coordinates => Target.Coordinates;
    public bool IsPlanet => Target.IsPlanet;

    /// <summary>Precomputed search keys (see <see cref="CatalogSearch"/>).</summary>
    public CatalogSearch.Entry Search { get; }

    /// <summary>Main cross-identifications (M, NGC, IC, Caldwell), e.g. "NGC 1976".</summary>
    public string CrossIdentifiers { get; }

    /// <summary>Apparent size, e.g. "177.8′ × 69.7′"; empty if unknown.</summary>
    public string SizeText { get; }

    /// <summary>Major axis in arcminutes, for the size filter; null if unknown.</summary>
    public double? MajorAxisArcmin => Target.MajorAxisArcmin;

    public double? SurfaceBrightness => Target.SurfaceBrightness;

    /// <summary>IAU constellation; for planets it is recalculated with the date.</summary>
    [ObservableProperty]
    public partial string Constellation { get; set; }

    /// <summary>Apparent magnitude: fixed for deep-sky objects; for planets it changes with the date. NaN if unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MagnitudeText))]
    public partial double ApparentMagnitude { get; private set; }

    /// <summary>The magnitude with one decimal; empty when it is unknown.</summary>
    public string MagnitudeText => double.IsNaN(ApparentMagnitude)
        ? string.Empty
        : ApparentMagnitude.ToString("F1", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>True if the logbook has at least one observation of the object.</summary>
    [ObservableProperty]
    public partial bool IsObserved { get; set; }

    [ObservableProperty]
    public partial double AltitudeDegrees { get; private set; }

    [ObservableProperty]
    public partial double AzimuthDegrees { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibilityStatus))]
    public partial bool IsVisible { get; private set; }

    /// <summary>Angular distance to the Moon (degrees).</summary>
    [ObservableProperty]
    public partial double MoonSeparationDegrees { get; private set; }

    /// <summary>True if the Moon, bright and close, makes the object hard to observe.</summary>
    [ObservableProperty]
    public partial bool MoonInterferes { get; private set; }

    /// <summary>What the object does during the current night; null until calculated.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumAltitudeTonight), nameof(BestWindowText), nameof(IsObservableTonight))]
    public partial NightEphemeris? Night { get; set; }

    /// <summary>Highest altitude between sunset and sunrise; null until calculated.</summary>
    public double? MaximumAltitudeTonight => Night?.MaximumAltitudeDegrees;

    /// <summary>Best window in darkness, e.g. "21:20–05:05"; empty if there is none.</summary>
    public string BestWindowText => DisplayNames.Window(Night?.BestWindowStartLocal, Night?.BestWindowEndLocal);

    public bool IsObservableTonight => Night?.IsObservableInDarkness == true;

    public string VisibilityStatus => IsVisible ? Strings.StatusVisible : Strings.StatusNotVisible;

    /// <summary>Capture feasibility tonight with the active rig (spec §8); null until calculated or without a rig.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerdictText), nameof(Score), nameof(FitsFov), nameof(Verdict), nameof(RecommendedText))]
    public partial FeasibilityReport? Feasibility { get; set; }

    public FeasibilityVerdict? Verdict => Feasibility?.Verdict;

    /// <summary>"Good", "Unlikely"…; empty until calculated.</summary>
    public string VerdictText => Feasibility is { } report ? FeasibilityText.Verdict(report.Verdict) : string.Empty;

    /// <summary>Feasibility score 0–100; null until calculated.</summary>
    public int? Score => Feasibility?.Score;

    /// <summary>Suggested capture ("120 × 10 s at ISO 800 = 20 min · best 21:20–05:05"); empty without a report.</summary>
    public string RecommendedText => Feasibility is { } report ? FeasibilityText.Recommended(report) : string.Empty;

    /// <summary>The object fits the active rig's frame without being too small for it.</summary>
    public bool FitsFov => Feasibility?.Framing.Class is FramingClass.Good or FramingClass.Small;

    /// <summary>
    /// Updates the position at the chosen time. The object is visible when it is above the
    /// minimum altitude and above the local horizon in its direction.
    /// </summary>
    public void UpdatePosition(
        HorizontalCoordinates coordinates,
        double minimumAltitude,
        double moonSeparation,
        bool moonInterferes,
        double apparentMagnitude,
        HorizonMask? horizon = null)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        AltitudeDegrees = coordinates.AltitudeDegrees;
        AzimuthDegrees = coordinates.AzimuthDegrees;
        IsVisible = AltitudeDegrees >= minimumAltitude
                    && (horizon is null || horizon.IsFlat || AltitudeDegrees >= horizon.AltitudeAt(AzimuthDegrees));
        MoonSeparationDegrees = moonSeparation;
        MoonInterferes = moonInterferes;
        ApparentMagnitude = apparentMagnitude;
    }
}
