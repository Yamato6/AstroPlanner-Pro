using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Models;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// A catalog object with its position at the chosen time. One instance per object,
/// shared by the targets table, the plan, the sky chart and the detail.
/// </summary>
public sealed partial class CelestialObjectRowViewModel : ObservableObject
{
    public CelestialObjectRowViewModel(CelestialObject target)
    {
        Target = target;
        ApparentMagnitude = target.ApparentMagnitude;
    }

    public CelestialObject Target { get; }
    public string Id => Target.Id;
    public string CommonName => Target.CommonName;
    public string Constellation => Target.Constellation;
    public CelestialObjectType Type => Target.Type;
    public EquatorialCoordinates Coordinates => Target.Coordinates;
    public bool IsPlanet => Target.IsPlanet;

    /// <summary>Apparent magnitude: fixed for deep-sky objects; for planets it changes with the date.</summary>
    [ObservableProperty]
    public partial double ApparentMagnitude { get; private set; }

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

    public string VisibilityStatus => IsVisible ? "Visible" : "No visible";

    public void UpdatePosition(
        HorizontalCoordinates coordinates,
        double minimumAltitude,
        double moonSeparation,
        bool moonInterferes,
        double apparentMagnitude)
    {
        AltitudeDegrees = coordinates.AltitudeDegrees;
        AzimuthDegrees = coordinates.AzimuthDegrees;
        IsVisible = AltitudeDegrees >= minimumAltitude;
        MoonSeparationDegrees = moonSeparation;
        MoonInterferes = moonInterferes;
        ApparentMagnitude = apparentMagnitude;
    }
}
