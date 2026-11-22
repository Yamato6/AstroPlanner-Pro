namespace AstroPlanner.UI.Sky;

/// <summary>The web page a <see cref="SkyHost"/> shows (both are built from src/web).</summary>
public enum SkyPage
{
    /// <summary>index.html: the sky alone, inside the v2 Sky tab.</summary>
    Classic,

    /// <summary>planetarium.html: the v3 React UI over the full-window sky (<c>--ui=web</c>).</summary>
    Planetarium,
}
