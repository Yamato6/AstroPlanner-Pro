namespace AstroPlanner.DataBuilder;

/// <summary>A raw data file: where it comes from, its pinned checksum and its license.</summary>
/// <param name="RelativePath">Path under <c>data/raw</c>.</param>
internal sealed record DataSource(string RelativePath, Uri Url, string Sha256, string Version, string License);

/// <summary>
/// The pinned sources of the catalogs (spec §7.1). Every URL points to a tag or commit and is
/// verified with SHA-256, so a build is reproducible. Keep in sync with data/SOURCES.md.
/// The World Atlas 2015 light-pollution data is downloaded by hand (see SOURCES.md) and
/// processed in M3.
/// </summary>
internal static class DataSources
{
    private const string OpenNgcBase = "https://raw.githubusercontent.com/mattiaverga/OpenNGC/v20260501/";
    private const string D3CelestialBase = "https://raw.githubusercontent.com/ofrohn/d3-celestial/v0.7.32/data/";

    public static readonly DataSource OpenNgc = new(
        "openngc/NGC.csv", new Uri(OpenNgcBase + "database_files/NGC.csv"),
        "840fe0c9ee1332e551b2e722a0e92726cd7b157914a3d2177602832aadd3aa9e", "v20260501", "CC BY-SA 4.0");

    public static readonly DataSource OpenNgcAddendum = new(
        "openngc/addendum.csv", new Uri(OpenNgcBase + "database_files/addendum.csv"),
        "1d8f0914e643ada325a5a94d88d8fefad6a4937a2f77cc34f21483af22b11983", "v20260501", "CC BY-SA 4.0");

    public static readonly DataSource Hyg = new(
        "hyg/hyg_v44.csv.gz",
        new Uri("https://codeberg.org/astronexus/hyg/media/commit/53e3df311869e813ace5f1ad2ec4ce909f13256c/data/hyg/CURRENT/hyg_v44.csv.gz"),
        "00b349893b9a53106dd488d8371e8d2fa586043e500bb3cdb8bff3931682197d", "v4.4", "CC BY-SA 4.0");

    /// <summary>d3-celestial files copied as-is to data/build/sky for the sky renderer (M2).</summary>
    public static readonly IReadOnlyList<DataSource> D3Celestial =
    [
        new("d3-celestial/constellations.json", new Uri(D3CelestialBase + "constellations.json"),
            "11e14f1ee19b71bff5ca8af3f6e6a20596c4609dd86d43c9a796054fce88030c", "v0.7.32", "BSD-3-Clause"),
        new("d3-celestial/constellations.lines.json", new Uri(D3CelestialBase + "constellations.lines.json"),
            "294f66bef5d5cf50b1e17f16d2efa1d97a15131612c68dd935adef6e7373e13c", "v0.7.32", "BSD-3-Clause"),
        new("d3-celestial/constellations.bounds.json", new Uri(D3CelestialBase + "constellations.bounds.json"),
            "f2e2687af6b20b24567879f838c21874d412efcc93ecc1966be07e78431cc196", "v0.7.32", "BSD-3-Clause"),
        new("d3-celestial/mw.json", new Uri(D3CelestialBase + "mw.json"),
            "aee221a7a0e879418e685de00c3e68fbdfac5667c0a8aab74929ef9cf4aab4fb", "v0.7.32", "BSD-3-Clause"),
    ];

    public static IEnumerable<DataSource> All => new[] { OpenNgc, OpenNgcAddendum, Hyg }.Concat(D3Celestial);
}
