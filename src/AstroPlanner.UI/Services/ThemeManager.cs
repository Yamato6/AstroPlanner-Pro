using System.Windows;
using AstroPlanner.Core.Models;

namespace AstroPlanner.UI.Services;

/// <summary>
/// Swaps the colors of the running app: Themes/Night.xaml is merged after Themes/Dark.xaml (its
/// brushes win) or removed again. Every brush is used through DynamicResource, so the open
/// windows repaint at once; the native title bars are updated too.
/// </summary>
internal static class ThemeManager
{
    private static readonly Uri NightSource = new("pack://application:,,,/AstroPlannerPro;component/Themes/Night.xaml");

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Apply(ResourceDictionary resources, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var merged = resources.MergedDictionaries;
        var night = merged.FirstOrDefault(IsNight);
        if (theme == AppTheme.Night && night is null)
        {
            merged.Add(new ResourceDictionary { Source = NightSource });
        }
        else if (theme != AppTheme.Night && night is not null)
        {
            merged.Remove(night);
        }

        Current = theme;
    }

    /// <summary>Applies <paramref name="theme"/> to the application and to the title bar of every open window.</summary>
    public static void Apply(System.Windows.Application application, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(application);
        Apply(application.Resources, theme);
        foreach (Window window in application.Windows)
        {
            NativeWindows.ApplyTitleBar(window, theme);
        }
    }

    private static bool IsNight(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.EndsWith("Night.xaml", StringComparison.OrdinalIgnoreCase) == true;
}
