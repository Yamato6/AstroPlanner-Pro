using AstroPlanner.Core.Models;

namespace AstroPlanner.UI.Services;

/// <summary>
/// The theme in use (dark or night vision). The settings set it; the window
/// (<see cref="ThemeManager"/>), the charts and the 3D sky follow <see cref="Changed"/>.
/// </summary>
public sealed class ThemeState
{
    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public bool IsNight => Current == AppTheme.Night;

    /// <summary>The renderer's theme name (<c>setTheme</c>, <c>init</c>).</summary>
    public string SkyTheme => IsNight ? "night" : "dark";

    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    public void Apply(AppTheme theme)
    {
        if (theme == Current)
        {
            return;
        }

        Current = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
