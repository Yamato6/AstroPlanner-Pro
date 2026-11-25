using System.ComponentModel;
using System.Globalization;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The <c>selection.*</c> area: the planetarium's object card. It reads the selected row of
/// Targets (or what only the sky selected: the Sun, the Moon, a satellite) with the texts the
/// classic window shows, and acts through the same commands: centre, frame with the camera,
/// add to or remove from the plan, clear. <c>selection.changed</c> follows the selection, its
/// row (feasibility, tonight's ephemeris) and the plan.
/// </summary>
public sealed class SelectionEndpoints : IEndpointModule
{
    /// <summary>The card lists at most this many feasibility reasons (the most important first).</summary>
    public const int MaximumReasons = 4;

    private readonly SkyViewModel _sky;
    private readonly TargetsViewModel _targets;
    private readonly ObjectDetailViewModel _detail;
    private readonly ChangeFeed _changes;
    private CelestialObjectRowViewModel? _row;
    private RpcDispatcher? _rpc;

    public SelectionEndpoints(ShellViewModel shell, IUiDispatcher ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _sky = shell.Sky;
        _targets = shell.Targets;
        _detail = shell.Detail;
        _changes = new ChangeFeed(ui, time, revision => _rpc?.Publish(AppEvents.SelectionChanged, new SelectionChangedEvent(revision)));
    }

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.SelectionGet, (_, _) => Answer());
        rpc.Register(AppMethods.SelectionCenter, (_, _) => Run(() => _sky.CenterOnSelectionCommand.Execute(null)));
        rpc.Register(AppMethods.SelectionFrame, (_, _) => Run(() =>
        {
            _sky.Camera.IsFovVisible = true;
            _sky.CenterOnSelectionCommand.Execute(null);
        }));
        rpc.Register(AppMethods.SelectionAddToPlan, (_, _) => Run(() => _detail.AddToPlanCommand.Execute(null)));
        rpc.Register(AppMethods.SelectionRemoveFromPlan, (_, _) => Run(() => _detail.RemoveFromPlanCommand.Execute(null)));
        rpc.Register(AppMethods.SelectionClear, (_, _) => Run(_sky.ClearSelection));

        _targets.PropertyChanged += OnTargetsChanged;
        _sky.PropertyChanged += OnSkyChanged;
        _detail.PropertyChanged += OnDetailChanged;
        Follow(_targets.SelectedObject);
    }

    /// <summary>The card as a contract (read on the UI thread).</summary>
    public SelectionSnapshot Snapshot()
    {
        if (_sky.SkyOnlySelectionText is { Length: > 0 } skyOnly)
        {
            return new SelectionSnapshot(_changes.Revision, Only(skyOnly));
        }

        return new SelectionSnapshot(_changes.Revision, _targets.SelectedObject is { } row ? Card(row) : null);
    }

    private static SelectedObject Only(string title) =>
        new(null, Text(title, 200)!, null, null, null, null, null, null, null, null, null, null, null, null, null, [], false);

    private SelectedObject Card(CelestialObjectRowViewModel row)
    {
        var report = row.Feasibility;
        var night = row.Night;
        return new SelectedObject(
            Text(row.Id, 64),
            Text(row.Title, 200)!,
            Text(DisplayNames.Of(row.Type), 80),
            Text(row.Constellation, 80),
            double.IsNaN(row.ApparentMagnitude) ? null : Text(row.MagnitudeText, 16),
            Text(DisplayNames.RaDec(row.Coordinates), 80),
            Finite(row.AltitudeDegrees) is { } altitude ? Math.Clamp(altitude, -90, 90) : null,
            Finite(row.AzimuthDegrees) is { } azimuth ? (azimuth % 360 + 360) % 360 : null,
            night?.TransitLocal?.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            night is null ? null : Math.Clamp(night.MaximumAltitudeDegrees, -90, 90),
            Text(row.BestWindowText, 80),
            report is null ? null : Verdict(report.Verdict),
            report is null ? null : Text(row.VerdictText, 40),
            report?.Score is { } score ? Math.Clamp(score, 0, 100) : null,
            Text(row.RecommendedText, 300),
            report is null ? [] : [.. report.Reasons.Take(MaximumReasons).Select(FeasibilityText.Line).Where(line => line.Length > 0)],
            _detail.IsInPlan);
    }

    internal static string Verdict(FeasibilityVerdict verdict) => verdict switch
    {
        FeasibilityVerdict.Excellent => FeasibilityVerdicts.Excellent,
        FeasibilityVerdict.Good => FeasibilityVerdicts.Good,
        FeasibilityVerdict.Marginal => FeasibilityVerdicts.Marginal,
        FeasibilityVerdict.Unlikely => FeasibilityVerdicts.Unlikely,
        _ => FeasibilityVerdicts.NotPossible,
    };

    /// <summary>Null for an empty text; cut to the contract's length otherwise.</summary>
    private static string? Text(string? text, int length) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= length ? text : text[..length];

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    private Task<SelectionSnapshot> Answer() => Task.FromResult(Snapshot());

    private Task<SelectionSnapshot> Run(Action command)
    {
        command();
        return Answer();
    }

    /// <summary>Listens to the selected row, whose feasibility and ephemeris arrive in the background.</summary>
    private void Follow(CelestialObjectRowViewModel? row)
    {
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
        }

        _row = row;
        if (_row is not null)
        {
            _row.PropertyChanged += OnRowChanged;
        }
    }

    private void OnTargetsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetsViewModel.SelectedObject))
        {
            Follow(_targets.SelectedObject);
            _changes.Invalidate();
        }
    }

    private void OnSkyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SkyViewModel.SkyOnlySelectionText))
        {
            _changes.Invalidate();
        }
    }

    private void OnDetailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectDetailViewModel.IsInPlan))
        {
            _changes.Invalidate();
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => _changes.Invalidate();

    public void Dispose()
    {
        _targets.PropertyChanged -= OnTargetsChanged;
        _sky.PropertyChanged -= OnSkyChanged;
        _detail.PropertyChanged -= OnDetailChanged;
        Follow(null);
        _changes.Dispose();
        _rpc = null;
    }
}
