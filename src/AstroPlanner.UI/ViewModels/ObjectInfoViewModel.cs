using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// "About" of the selected object (spec §4.4): the Wikipedia lead with its image, the image's
/// author and license (Wikimedia Commons) and links to both pages. Shows nothing when Wikipedia has
/// no article about the object. Loads only while the detail is shown.
/// </summary>
public sealed partial class ObjectInfoViewModel : ObservableObject, IDisposable
{
    private readonly IObjectInfoService? _service;
    private readonly OnlineInfoState _online;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _loading;
    private CelestialObject? _target;
    private bool _isShown;
    private string? _shownId;

    public ObjectInfoViewModel(IObjectInfoService? service, OnlineInfoState online, IAppLogger logger, TimeProvider? timeProvider = null)
    {
        _service = service;
        _online = online;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _online.Changed += (_, _) => Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary), nameof(ImageCreditText))]
    [NotifyCanExecuteChangedFor(nameof(OpenArticleCommand), nameof(OpenImagePageCommand), nameof(OpenLicenseCommand))]
    public partial ObjectSummary? Summary { get; private set; }

    public bool HasSummary => Summary is not null;

    /// <summary>The image's bytes (JPEG), or null.</summary>
    [ObservableProperty]
    public partial byte[]? ImageContent { get; private set; }

    /// <summary>"Image: NASA, ESA… · Public domain (Wikimedia Commons)"; empty without an image.</summary>
    public string ImageCreditText => Summary?.Image is { } image
        ? DisplayNames.Format(Strings.InfoImageCreditFormat, image.Artist ?? Strings.InfoUnknownAuthor, image.License)
        : "";

    /// <summary>Progress, or why there is no description; empty when it is shown.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    public Task LoadTask { get; private set; } = Task.CompletedTask;

    public void Show(CelestialObject? target, bool isShown)
    {
        _target = target;
        _isShown = isShown;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSummary))]
    private void OpenArticle() => SafeLinks.Open(Summary?.ArticleUrl);

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void OpenImagePage() => SafeLinks.Open(Summary?.Image?.FilePageUrl);

    [RelayCommand(CanExecute = nameof(HasLicenseUrl))]
    private void OpenLicense() => SafeLinks.Open(Summary?.Image?.LicenseUrl);

    private bool HasImage() => Summary?.Image is not null;

    private bool HasLicenseUrl() => Summary?.Image?.LicenseUrl is not null;

    private void Refresh()
    {
        string? message = _service is null ? Strings.InfoUnavailable
            : !_online.Wikipedia.Enabled ? Strings.InfoDisabled
            : _target is null ? ""
            : null;
        if (message is not null)
        {
            Clear(message);
            return;
        }

        if (!_isShown || _target!.Id == _shownId)
        {
            return;
        }

        _loading?.Cancel();
        _loading?.Dispose();
        _shownId = _target.Id;
        Summary = null;
        ImageContent = null;
        StatusText = Strings.InfoLoading;
        var cancellation = _loading = new CancellationTokenSource();
        LoadTask = LoadAsync(_target, cancellation.Token);
    }

    public void Dispose()
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
    }

    private void Clear(string message)
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
        _shownId = null;
        Summary = null;
        ImageContent = null;
        StatusText = message;
    }

    private async Task LoadAsync(CelestialObject target, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _service!.GetSummaryAsync(target, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _online.Report(_online.Wikipedia, _time.GetUtcNow().UtcDateTime, null);
            Summary = summary;
            StatusText = summary is null ? Strings.InfoNoArticle : "";
            if (summary?.Image is { } image)
            {
                try
                {
                    var content = await _service.GetImageAsync(image, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    ImageContent = content;
                }
                catch (OnlineServiceException ex)
                {
                    _logger.LogInfo($"Wikipedia image unavailable: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Another object was selected.
        }
        catch (OnlineServiceException ex)
        {
            _online.Report(_online.Wikipedia, _time.GetUtcNow().UtcDateTime, ex.Message);
            _shownId = null;   // try again next time
            StatusText = DisplayNames.Format(Strings.InfoFailedFormat, ex.Message);
        }
    }
}
