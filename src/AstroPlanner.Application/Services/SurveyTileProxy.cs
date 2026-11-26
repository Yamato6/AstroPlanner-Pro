using System.Globalization;
using AstroPlanner.Application.Sky;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Application.Services;

/// <summary>
/// The survey tile proxy behind <c>https://tiles.astroplanner/</c> (spec §5.4, §8.3): the page asks
/// for HiPS tiles there and the host answers from the tile cache or the survey's mirrors
/// (<see cref="ISurveyTileSource"/>). Only well-formed requests for a known survey are served —
/// an order the survey has, a pixel that exists at that order, the HiPS directory of that pixel,
/// a JPEG — and anything else is answered "not found" without a network request. With online
/// images turned off only cached tiles are served.
/// </summary>
public sealed class SurveyTileProxy(ISurveyTileSource tiles, OnlineInfoState online)
{
    /// <summary>The lowest order the survey layer asks for (HiPS tiles start at order 3).</summary>
    public const int MinimumOrder = 3;

    /// <summary>
    /// Reads a tile request: <c>https://tiles.astroplanner/{survey}/Norder{o}/Dir{d}/Npix{n}.jpg</c> or the
    /// order-3 <c>…/{survey}/Norder3/Allsky.jpg</c>. False for anything else.
    /// </summary>
    public static bool TryParse(string? uri, IReadOnlyList<HipsSurvey> surveys, out HipsSurvey? survey, out HipsTileKey key)
    {
        ArgumentNullException.ThrowIfNull(surveys);
        survey = null;
        key = default;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.Equals(parsed.Host, SkyHosts.TilesHost, StringComparison.OrdinalIgnoreCase)
            || !parsed.IsDefaultPort
            || parsed.Query.Length > 0
            || parsed.Fragment.Length > 0
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        var parts = parsed.AbsolutePath.TrimStart('/').Split('/');
        if (parts.Length is not (3 or 4))
        {
            return false;
        }

        var found = surveys.FirstOrDefault(s => string.Equals(s.Id, parts[0], StringComparison.Ordinal));
        if (found is null || !TryNumber(parts[1], "Norder", out var order) || order < MinimumOrder || order > found.MaximumOrder)
        {
            return false;
        }

        if (parts.Length == 3)
        {
            if (order != HipsTileKey.Allsky.Order || !string.Equals(parts[2], "Allsky.jpg", StringComparison.Ordinal))
            {
                return false;
            }

            survey = found;
            key = HipsTileKey.Allsky;
            return true;
        }

        if (!parts[3].EndsWith(".jpg", StringComparison.Ordinal)
            || !TryNumber(parts[2], "Dir", out var directory)
            || !TryNumber(parts[3][..^4], "Npix", out var index)
            || index >= Healpix.PixelCount((int)order)
            || directory != index / 10000 * 10000)
        {
            return false;
        }

        survey = found;
        key = new HipsTileKey((int)order, index);
        return true;
    }

    /// <summary>The tile's JPEG, or null ("not found": a bad request, no connection, or online images off and not cached).</summary>
    public async Task<byte[]?> GetAsync(string? uri, CancellationToken cancellationToken = default)
    {
        if (!TryParse(uri, tiles.Surveys, out var survey, out var key))
        {
            return null;
        }

        if (!online.Images.Enabled)
        {
            return tiles.GetCachedTile(survey!, key)?.Content;
        }

        try
        {
            return (await tiles.GetTileAsync(survey!, key, cancellationToken).ConfigureAwait(false)).Content;
        }
        catch (OnlineServiceException)
        {
            return null;
        }
    }

    /// <summary>A prefix followed by a decimal number without sign, spaces or leading zeros (12 digits at most).</summary>
    private static bool TryNumber(string text, string prefix, out long value)
    {
        value = 0;
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = text[prefix.Length..];
        return digits.Length is > 0 and <= 12
               && digits.All(char.IsAsciiDigit)
               && (digits.Length == 1 || digits[0] != '0')
               && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
