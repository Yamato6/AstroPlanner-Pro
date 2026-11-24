using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Framing preview of the selected object with the active rig (spec §4.4): the rig's exact field
/// and aspect, rotated by the camera angle, from HiPS survey tiles, with the object's outline.
/// Loads only while the detail is shown, cancelling the previous request when the object, rig,
/// survey or rotation changes. Decoded tiles of the last previews are kept in memory so that
/// turning the camera is immediate.
/// </summary>
public sealed partial class FramingViewModel : ObservableObject, IDisposable
{
    /// <summary>Width of the preview in pixels (the height follows the sensor's aspect).</summary>
    public const int OutputWidth = 640;

    private const int DecodedTilesKept = 48;

    private readonly ISurveyTileSource? _tiles;
    private readonly IImageDecoder _decoder;
    private readonly EquipmentState? _equipment;
    private readonly OnlineInfoState _online;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly Dictionary<(string Survey, HipsTileKey Key), RgbImage> _decoded = [];
    private CancellationTokenSource? _loading;
    private CelestialObject? _target;
    private bool _isShown;
    private string? _shownKey;

    public FramingViewModel(
        ISurveyTileSource? tiles, IImageDecoder decoder, EquipmentState? equipment, OnlineInfoState online, IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        _tiles = tiles;
        _decoder = decoder;
        _equipment = equipment;
        _online = online;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        Surveys = tiles?.Surveys ?? [];
        SelectedSurvey = Surveys.Count > 0 ? Surveys[0] : null;
        if (_equipment is not null)
        {
            _equipment.Changed += (_, _) => Refresh();
        }

        _online.Changed += (_, _) => Refresh();
    }

    public IReadOnlyList<HipsSurvey> Surveys { get; }

    [ObservableProperty]
    public partial HipsSurvey? SelectedSurvey { get; set; }

    /// <summary>Camera angle: the image's up side from north through east, degrees.</summary>
    [ObservableProperty]
    public partial double RotationDegrees { get; set; }

    /// <summary>The preview, or null while there is none.</summary>
    [ObservableProperty]
    public partial RgbImage? Image { get; private set; }

    /// <summary>Why there is no (complete) preview, or progress; empty when the preview is complete.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    /// <summary>Credit of the survey and the mirror that served it.</summary>
    [ObservableProperty]
    public partial string AttributionText { get; private set; } = "";

    /// <summary>"Canon T7 + 650 mm · 2.08° × 1.39°".</summary>
    [ObservableProperty]
    public partial string FieldText { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>The object's outline (axes and position angle) in preview pixels; hidden when too small or unknown.</summary>
    [ObservableProperty]
    public partial bool HasOutline { get; private set; }

    [ObservableProperty]
    public partial double OutlineWidth { get; private set; }

    [ObservableProperty]
    public partial double OutlineHeight { get; private set; }

    [ObservableProperty]
    public partial double OutlineLeft { get; private set; }

    [ObservableProperty]
    public partial double OutlineTop { get; private set; }

    /// <summary>Clockwise rotation of the outline's width axis on screen, degrees.</summary>
    [ObservableProperty]
    public partial double OutlineAngle { get; private set; }

    /// <summary>The current load, for tests.</summary>
    public Task LoadTask { get; private set; } = Task.CompletedTask;

    partial void OnSelectedSurveyChanged(HipsSurvey? value) => Refresh();

    partial void OnRotationDegreesChanged(double value) => Refresh();

    /// <summary>The selected object and whether the detail is on screen.</summary>
    public void Show(CelestialObject? target, bool isShown)
    {
        _target = target;
        _isShown = isShown;
        Refresh();
    }

    private void Refresh()
    {
        var rig = _equipment?.ActiveRig;
        string? message = _tiles is null || SelectedSurvey is null ? Strings.FramingUnavailable
            : !_online.Images.Enabled ? Strings.FramingDisabled
            : _target is null ? ""
            : _target.IsMoving ? Strings.FramingPlanet
            : rig is null ? Strings.FramingNoRig
            : null;
        FieldText = rig is null ? "" : DisplayNames.Format(Strings.FramingFieldFormat, rig.Name, rig.FieldOfView.WidthDegrees, rig.FieldOfView.HeightDegrees);
        if (message is not null)
        {
            Cancel();
            _shownKey = null;
            Image = null;
            HasOutline = false;
            AttributionText = "";
            StatusText = message;
            return;
        }

        if (!_isShown)
        {
            return;   // loaded when the detail is shown
        }

        var request = new FramingRequest(_target!.Coordinates.RightAscensionHours * 15, _target.Coordinates.DeclinationDegrees,
            rig!.FieldOfView.WidthDegrees, rig.FieldOfView.HeightDegrees, RotationDegrees, OutputWidth);
        var key = $"{_target.Id}|{SelectedSurvey!.Id}|{request}";
        if (key == _shownKey)
        {
            return;
        }

        Cancel();
        _shownKey = key;
        var cancellation = _loading = new CancellationTokenSource();
        LoadTask = LoadAsync(_target, request, SelectedSurvey, cancellation.Token);
    }

    public void Dispose() => Cancel();

    private void Cancel()
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
        IsLoading = false;
    }

    private async Task LoadAsync(CelestialObject target, FramingRequest request, HipsSurvey survey, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await Task.Run(() => FramingPreview.Plan(request, survey), cancellationToken);
            var missing = plan.Tiles.Where(k => !_decoded.ContainsKey((survey.Id, k))).ToList();
            IsLoading = missing.Count > 0;
            if (missing.Count > 0)
            {
                StatusText = DisplayNames.Format(Strings.FramingLoadingFormat, missing.Count);
            }

            var fetched = await Task.WhenAll(missing.Select(k => FetchAsync(survey, k, cancellationToken)));
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var (key, image, _, _) in fetched.Where(f => f.Image is not null))
            {
                _decoded[(survey.Id, key)] = image!;
            }

            var tiles = plan.Tiles.Where(k => _decoded.ContainsKey((survey.Id, k))).ToDictionary(k => k, k => _decoded[(survey.Id, k)]);
            var (composed, coverage) = await Task.Run(() => (plan.Compose(tiles, out var c), c), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Trim(plan.Tiles.Select(k => (survey.Id, k)).ToHashSet());

            var error = fetched.Select(f => f.Error).FirstOrDefault(e => e is not null);
            var mirrors = fetched.Select(f => f.Source).Where(s => s is not null and not "cache" and not "CDS").Distinct().ToList();
            if (fetched.Length > 0)
            {
                _online.Report(_online.Images, _time.GetUtcNow().UtcDateTime, coverage > 0 || error is null ? null : error);
            }

            Image = coverage > 0 ? composed : null;
            AttributionText = coverage > 0
                ? _tiles!.CreditFor(survey) + string.Concat(mirrors.Select(m => DisplayNames.Format(Strings.FramingMirrorFormat, m)))
                : "";
            StatusText = coverage >= 1 ? ""
                : coverage > 0 ? Strings.FramingPartial
                : DisplayNames.Format(Strings.FramingFailedFormat, error ?? Strings.FramingNoTiles);
            UpdateOutline(target, plan, coverage > 0);
            if (coverage <= 0)
            {
                _shownKey = null;   // retried the next time it is shown
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer request replaced this one.
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            _logger.LogError("Framing preview failed.", ex);
            Image = null;
            HasOutline = false;
            StatusText = DisplayNames.Format(Strings.FramingFailedFormat, ex.Message);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private async Task<(HipsTileKey Key, RgbImage? Image, string? Source, string? Error)> FetchAsync(
        HipsSurvey survey, HipsTileKey key, CancellationToken cancellationToken)
    {
        try
        {
            var tile = await _tiles!.GetTileAsync(survey, key, cancellationToken);
            var image = await Task.Run(() => _decoder.Decode(tile.Content), cancellationToken);
            return (key, image, tile.Source, null);
        }
        catch (OnlineServiceException ex)
        {
            return (key, null, null, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogInfo($"Survey tile {key} could not be decoded: {ex.Message}");
            return (key, null, null, Strings.FramingInvalidTile);
        }
    }

    /// <summary>Keeps the decoded tiles of the current preview, plus others up to the limit.</summary>
    private void Trim(HashSet<(string, HipsTileKey)> current)
    {
        foreach (var stale in _decoded.Keys.Where(k => !current.Contains(k)).Take(Math.Max(0, _decoded.Count - DecodedTilesKept)).ToList())
        {
            _decoded.Remove(stale);
        }
    }

    private void UpdateOutline(CelestialObject target, FramingPlan plan, bool visible)
    {
        if (!visible || target.MajorAxisArcmin is not > 0 || target.MajorAxisArcmin is not { } major)
        {
            HasOutline = false;
            return;
        }

        var minor = target.MinorAxisArcmin is > 0 ? target.MinorAxisArcmin.Value : major;
        OutlineWidth = plan.PixelsFor(major);
        OutlineHeight = plan.PixelsFor(minor);
        OutlineLeft = (plan.Width - OutlineWidth) / 2;
        OutlineTop = (plan.Height - OutlineHeight) / 2;
        // The width axis is drawn horizontally (90° clockwise from up); turn it to the major axis.
        OutlineAngle = plan.ScreenAngleDegrees(target.PositionAngleDegrees ?? 0) - 90;
        HasOutline = OutlineWidth >= 6 && OutlineWidth <= 3 * plan.Width;
    }
}
