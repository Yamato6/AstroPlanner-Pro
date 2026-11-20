namespace AstroPlanner.UI.Services;

/// <summary>Names of the per-service HttpClients registered in App.xaml.cs (spec §9).</summary>
public static class OnlineClients
{
    public const string OpenMeteo = "open-meteo";
    public const string SevenTimer = "7timer";
    public const string GitHub = "github";

    public static IReadOnlyList<string> All { get; } = [OpenMeteo, SevenTimer, GitHub];
}
