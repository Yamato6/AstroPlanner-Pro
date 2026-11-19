namespace AstroPlanner.Core.Models;

/// <summary>One of the 88 IAU constellations.</summary>
/// <param name="Abbreviation">Official three-letter IAU abbreviation (e.g. "UMa").</param>
/// <param name="Name">Official IAU name (e.g. "Ursa Major").</param>
public sealed record Constellation(string Abbreviation, string Name);
