namespace AstroPlanner.Core.Models;

/// <summary>Landscape ids of a site (<see cref="Location.LandscapeId"/>).</summary>
public static class Landscapes
{
    /// <summary>No landscape and no profile: a flat ground at 0°.</summary>
    public const string Flat = "flat";

    public const int MaximumIdLength = 40;

    /// <summary>Null, or a lower-case id of letters, digits and hyphens (a file name of the media folder).</summary>
    public static string? Validate(string? id) =>
        id is null || IsValidId(id)
            ? id
            : throw new ArgumentException("A landscape id is lower-case letters, digits and hyphens.", nameof(id));

    public static bool IsValidId(string id) =>
        !string.IsNullOrEmpty(id) && id.Length <= MaximumIdLength
        && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
