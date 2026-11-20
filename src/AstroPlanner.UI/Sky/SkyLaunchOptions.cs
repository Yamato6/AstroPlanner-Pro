using System.Globalization;
using System.Text.RegularExpressions;
using AstroPlanner.SkyBridge;

namespace AstroPlanner.UI.Sky;

/// <summary>
/// Command-line switches for the renderer benchmark (docs/sky-renderer.md), turned into the query
/// string of the renderer page. Only known switches with well-formed values pass; nothing else
/// (and no user data) ever reaches the URL.
/// <list type="bullet">
/// <item><c>--sky-benchmark</c>: 10 s camera sweep, frame statistics logged;</item>
/// <item><c>--sky-stars=N</c>: the star catalogue drawn N times (stress test, 1–8);</item>
/// <item><c>--sky-resolution=WxH</c>: render at a fixed size (e.g. 1920x1080) whatever the window;</item>
/// <item><c>--sky-mode=dome|allSky|finder</c>: benchmark one projection.</item>
/// </list>
/// </summary>
public static partial class SkyLaunchOptions
{
    public static string Query(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var parameters = new List<string>();
        if (args.Contains("--sky-benchmark", StringComparer.OrdinalIgnoreCase))
        {
            parameters.Add("bench=1");
        }

        if (Value(args, "--sky-stars=") is { } stars && int.TryParse(stars, NumberStyles.Integer, CultureInfo.InvariantCulture, out var multiplier))
        {
            parameters.Add(string.Create(CultureInfo.InvariantCulture, $"stars={Math.Clamp(multiplier, 1, 8)}"));
        }

        if (Value(args, "--sky-resolution=") is { } resolution && Resolution().Match(resolution) is { Success: true } size)
        {
            parameters.Add("res=" + size.Groups[1].Value + "x" + size.Groups[2].Value);
        }

        if (Value(args, "--sky-mode=") is { } mode
            && new[] { SkyViewModes.Dome, SkyViewModes.AllSky, SkyViewModes.Finder }
                .FirstOrDefault(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase)) is { } known)
        {
            parameters.Add("mode=" + known);
        }

        return parameters.Count == 0 ? "" : "?" + string.Join('&', parameters);
    }

    private static string? Value(IReadOnlyList<string> args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    [GeneratedRegex("^([0-9]{3,4})x([0-9]{3,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex Resolution();
}