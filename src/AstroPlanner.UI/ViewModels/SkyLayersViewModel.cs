using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.SkyBridge;
using AstroPlanner.UI.Resources;

namespace AstroPlanner.UI.ViewModels;

/// <summary>A visible-layer switch of the sky view.</summary>
public sealed partial class SkyLayerToggle(string key, string name, bool isEnabled, Action changed) : ObservableObject
{
    public string Key { get; } = key;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = isEnabled;

    partial void OnIsEnabledChanged(bool value) => changed();
}

/// <summary>
/// The sky's layers (stars, constellations, grids…), the star limit and the statistics overlay:
/// saved with the view settings and sent to the renderer as they change.
/// </summary>
public sealed partial class SkyLayersViewModel : ObservableObject
{
    private readonly SkySession _session;
    private readonly bool _initialized;

    internal SkyLayersViewModel(SkySession session)
    {
        _session = session;
        var settings = session.Settings;
        StarLimitMagnitude = settings.StarLimitMagnitude;
        ShowStats = settings.ShowStats;
        LayerToggles =
        [
            Toggle("stars", Strings.SkyLayerStars, settings.Stars),
            Toggle("constellationLines", Strings.SkyLayerConstellationLines, settings.ConstellationLines),
            Toggle("constellationNames", Strings.SkyLayerConstellationNames, settings.ConstellationNames),
            Toggle("constellationBoundaries", Strings.SkyLayerConstellationBoundaries, settings.ConstellationBoundaries),
            Toggle("milkyWay", Strings.SkyLayerMilkyWay, settings.MilkyWay),
            Toggle("deepSky", Strings.SkyLayerDeepSky, settings.DeepSky),
            Toggle("bodies", Strings.SkyLayerBodies, settings.Bodies),
            Toggle("satellites", Strings.SkyLayerSatellites, settings.Satellites),
            Toggle("labels", Strings.SkyLayerLabels, settings.Labels),
            Toggle("altAzGrid", Strings.SkyLayerAltAzGrid, settings.AltAzGrid),
            Toggle("equatorialGrid", Strings.SkyLayerEquatorialGrid, settings.EquatorialGrid),
            Toggle("ecliptic", Strings.SkyLayerEcliptic, settings.Ecliptic),
            Toggle("meridian", Strings.SkyLayerMeridian, settings.Meridian),
            Toggle("ground", Strings.SkyLayerGround, settings.Ground),
            Toggle("skyBrightness", Strings.SkyLayerSkyBrightness, settings.SkyBrightness)
        ];
        _initialized = true;
    }

    public IReadOnlyList<SkyLayerToggle> LayerToggles { get; }

    [ObservableProperty]
    public partial double StarLimitMagnitude { get; set; }

    partial void OnStarLimitMagnitudeChanged(double value) => OnLayersChanged();

    [ObservableProperty]
    public partial bool ShowStats { get; set; }

    partial void OnShowStatsChanged(bool value)
    {
        if (!_initialized)
        {
            return;
        }

        _session.Settings.ShowStats = value;
        _session.SaveSettings();
    }

    /// <summary>The layers as the renderer expects them (<c>init</c>, <c>setLayers</c>).</summary>
    public SkyLayers Layers() => new(
        Stars: Layer("stars"),
        ConstellationLines: Layer("constellationLines"),
        ConstellationBoundaries: Layer("constellationBoundaries"),
        ConstellationNames: Layer("constellationNames"),
        AltAzGrid: Layer("altAzGrid"),
        EquatorialGrid: Layer("equatorialGrid"),
        Ecliptic: Layer("ecliptic"),
        Meridian: Layer("meridian"),
        MilkyWay: Layer("milkyWay"),
        DeepSky: Layer("deepSky"),
        Bodies: Layer("bodies"),
        Labels: Layer("labels"),
        Ground: Layer("ground"),
        SkyBrightness: Layer("skyBrightness"),
        Satellites: Layer("satellites"),
        StarLimitMagnitude: Math.Clamp(StarLimitMagnitude, 3, 9.5),
        DeepSkyLimitMagnitude: _session.Settings.DeepSkyLimitMagnitude);

    private SkyLayerToggle Toggle(string key, string name, bool value) => new(key, name, value, OnLayersChanged);

    private bool Layer(string key) => LayerToggles?.FirstOrDefault(t => t.Key == key)?.IsEnabled ?? true;

    private void OnLayersChanged()
    {
        if (!_initialized)
        {
            return;
        }

        var layers = Layers();
        var settings = _session.Settings;
        settings.Stars = layers.Stars;
        settings.ConstellationLines = layers.ConstellationLines;
        settings.ConstellationBoundaries = layers.ConstellationBoundaries;
        settings.ConstellationNames = layers.ConstellationNames;
        settings.AltAzGrid = layers.AltAzGrid;
        settings.EquatorialGrid = layers.EquatorialGrid;
        settings.Ecliptic = layers.Ecliptic;
        settings.Meridian = layers.Meridian;
        settings.MilkyWay = layers.MilkyWay;
        settings.DeepSky = layers.DeepSky;
        settings.Bodies = layers.Bodies;
        settings.Labels = layers.Labels;
        settings.Ground = layers.Ground;
        settings.SkyBrightness = layers.SkyBrightness;
        settings.Satellites = layers.Satellites;
        settings.StarLimitMagnitude = layers.StarLimitMagnitude;
        _session.SaveSettings();
        _session.Send(SkyMessageCodec.SetLayers(layers));
    }
}
