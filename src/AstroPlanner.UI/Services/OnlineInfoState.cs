namespace AstroPlanner.UI.Services;

/// <summary>Last outcome of an on-demand online service, for the status tooltip.</summary>
public sealed class OnlineServiceStatus
{
    public bool Enabled { get; set; } = true;

    /// <summary>When it last answered (from the network or its cache), UTC.</summary>
    public DateTime? LastSuccessUtc { get; private set; }

    /// <summary>Reason of the last failure, cleared by the next success.</summary>
    public string? LastError { get; private set; }

    public void Succeeded(DateTime utc)
    {
        LastSuccessUtc = utc;
        LastError = null;
    }

    public void Failed(string error) => LastError = error;
}

/// <summary>
/// The on-demand online services of M4b (spec §4.4, §4.2, §9): survey images, Wikipedia and Sesame.
/// Holds their toggles (from Settings) and last outcome; raises <see cref="Changed"/> for the top-bar status.
/// </summary>
public sealed class OnlineInfoState
{
    public OnlineServiceStatus Images { get; } = new();
    public OnlineServiceStatus Wikipedia { get; } = new();
    public OnlineServiceStatus NameResolver { get; } = new();

    /// <summary>A toggle or an outcome changed.</summary>
    public event EventHandler? Changed;

    public void Apply(bool images, bool wikipedia, bool nameResolver)
    {
        var changed = Images.Enabled != images || Wikipedia.Enabled != wikipedia || NameResolver.Enabled != nameResolver;
        Images.Enabled = images;
        Wikipedia.Enabled = wikipedia;
        NameResolver.Enabled = nameResolver;
        if (changed)
        {
            OnChanged();
        }
    }

    public void Report(OnlineServiceStatus service, DateTime utc, string? error)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (error is null)
        {
            service.Succeeded(utc);
        }
        else
        {
            service.Failed(error);
        }

        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
