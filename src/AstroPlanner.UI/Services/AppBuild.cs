namespace AstroPlanner.UI.Services;

/// <summary>
/// Facts about this executable. The version must be read from the exe's own assembly (the one
/// whose csproj sets &lt;Version&gt; and which CI stamps with the tag), not from a library's.
/// </summary>
public static class AppBuild
{
    /// <summary>Version of this executable (set by CI from the tag).</summary>
    public static AppVersion Version { get; } = AppVersion.Of(typeof(AppBuild).Assembly);
}
