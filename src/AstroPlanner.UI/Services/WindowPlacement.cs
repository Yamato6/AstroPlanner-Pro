using System.IO;
using System.Text.Json;
using System.Windows;

namespace AstroPlanner.UI.Services;

/// <summary>Size, position and state of the main window between sessions.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized)
{
    /// <summary>How much of the title bar must stay visible for the window to be draggable.</summary>
    private const double MinVisible = 120;

    /// <summary>
    /// Fits the window to the current desktop (<paramref name="virtualScreen"/>: all monitors).
    /// If it ended up outside, e.g. because a monitor was disconnected, it is centered on the
    /// primary work area and fitted to it.
    /// </summary>
    public WindowPlacement FitTo(Rect virtualScreen, Rect workArea, double minWidth, double minHeight)
    {
        // Limited by the whole desktop, not by the primary monitor: a large window on a
        // larger second monitor keeps its size.
        var width = Math.Clamp(Width, minWidth, Math.Max(minWidth, virtualScreen.Width));
        var height = Math.Clamp(Height, minHeight, Math.Max(minHeight, virtualScreen.Height));

        // Part of the title bar (top strip) must be inside the desktop.
        var titleBar = new Rect(Left, Top, width, 32);
        titleBar.Intersect(virtualScreen);
        var reachable = !titleBar.IsEmpty && titleBar.Width >= Math.Min(MinVisible, width) && Top >= virtualScreen.Top;
        if (reachable)
        {
            return this with { Width = width, Height = height };
        }

        width = Math.Clamp(width, minWidth, Math.Max(minWidth, workArea.Width));
        height = Math.Clamp(height, minHeight, Math.Max(minHeight, workArea.Height));
        return this with
        {
            Width = width,
            Height = height,
            Left = workArea.Left + (workArea.Width - width) / 2,
            Top = workArea.Top + (workArea.Height - height) / 2
        };
    }
}

/// <summary>Stores the window position in %LocalAppData%\AstroPlanner\window.json.</summary>
public sealed class WindowPlacementStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? AppPaths.DataFile("window.json");

    public WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            var placement = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_filePath));
            return placement is { Width: > 0, Height: > 0 } && double.IsFinite(placement.Left) && double.IsFinite(placement.Top)
                ? placement
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;   // no saved position: the window is centered
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(placement));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the position must not prevent closing the app.
        }
    }
}
