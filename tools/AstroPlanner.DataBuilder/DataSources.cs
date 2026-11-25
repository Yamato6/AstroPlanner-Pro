namespace AstroPlanner.DataBuilder;

/// <summary>A raw data file: where it comes from, its pinned checksum and its license.</summary>
/// <param name="RelativePath">Path under <c>data/raw</c>.</param>
internal sealed record DataSource(string RelativePath, Uri Url, string Sha256, string Version, string License);

/// <summary>
/// The pinned sources of the catalogs (spec §7.1). Every URL points to a tag or commit and is
/// verified with SHA-256, so a build is reproducible. Keep in sync with data/SOURCES.md.
/// The World Atlas 2015 light-pollution GeoTIFF (<see cref="WorldAtlas"/>) is downloaded and
/// unzipped by hand (653 MB zip, 3 GB GeoTIFF; see SOURCES.md) and only checked by build-lp-grid.
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

    /// <summary>
    /// Washington Double Star Catalog summary (USNO, served by Georgia State University; M6a). Only
    /// over HTTP (the HTTPS certificate does not validate): the pinned SHA-256 guarantees the content.
    /// </summary>
    public static readonly DataSource Wds = new(
        "wds/wdsweb_summ2.txt", new Uri("http://www.astro.gsu.edu/wds/Webtextfiles/wdsweb_summ2.txt"),
        "937e5ffaba92e4bcee791b92c6b321f698dc7d048a5e44c96ce9684470aa544f", "2026-10-29", "Public domain (U.S. Naval Observatory)");

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

    /// <summary>
    /// World Atlas 2015 artificial sky brightness (Falchi et al. 2016), the GeoTIFF inside the zip
    /// at <see cref="DataSource.Url"/>. Not part of <see cref="All"/>: never downloaded automatically.
    /// </summary>
    public static readonly DataSource WorldAtlas = new(
        "World_Atlas_2015.tif", new Uri("https://datapub.gfz.de/download/10.5880.GFZ.1.4.2016.001/World_Atlas_2015.zip"),
        "9725566ed4fcf1a8fa311654143bb024c4fe2a792b85356be99adce07e1e83c1", "2015 (published 2016)", "CC BY-NC 4.0");

    public static IEnumerable<DataSource> All => new[] { OpenNgc, OpenNgcAddendum, Hyg, Wds }.Concat(D3Celestial);

    /// <summary>
    /// "The Milky Way panorama" by ESO/S. Brunier (eso0932a, 2009): 6000×3000 JPEG, equirectangular in
    /// galactic coordinates (l = 0 in the middle, l growing to the left). Reprojected by build-media (R2c).
    /// </summary>
    public static readonly DataSource MilkyWayPanorama = new(
        "eso/eso0932a.jpg", new Uri("https://cdn.eso.org/images/large/eso0932a.jpg"),
        "60400c92c54b7c1bd12299c69e83b16e5b6256e7dabacc478c021758ecd28179", "eso0932a (2009-09-14)", "CC BY 4.0");

    private const string PolyHavenBase = "https://dl.polyhaven.org/file/ph-assets/HDRIs/extra/Tonemapped%20JPG/";

    /// <summary>
    /// Poly Haven HDRIs, as their tonemapped 8192×4096 JPEG (equirectangular). Each asset is CC0;
    /// the authors are credited anyway (data/curated/landscapes.json).
    /// </summary>
    public static readonly IReadOnlyList<DataSource> Landscapes =
    [
        new("polyhaven/noon_grass.jpg", new Uri(PolyHavenBase + "noon_grass.jpg"),
            "dfa12d5cb0d9c662484e8e7ef847f0a9a7112c02e542b667e4a29e0be857ac02", "noon_grass (2017-11-29)", "CC0 1.0"),
        new("polyhaven/kiara_5_noon.jpg", new Uri(PolyHavenBase + "kiara_5_noon.jpg"),
            "846ae4d00f93edaf01eb7a2e013aea283b87640924a22534f48d473168e03b20", "kiara_5_noon (2018-04-04)", "CC0 1.0"),
        new("polyhaven/goegap.jpg", new Uri(PolyHavenBase + "goegap.jpg"),
            "62919c5b11e78a52e0ce320b0d425bc44f9beb153bc6b0e561cc4115df60ddd1", "goegap (2017-10-02)", "CC0 1.0"),
        new("polyhaven/suburban_garden.jpg", new Uri(PolyHavenBase + "suburban_garden.jpg"),
            "10e50543f150678caa38bd38d87a72d3da18361d4a0d7329fdc902ae5c8cbb74", "suburban_garden (2026-02-23)", "CC0 1.0"),
    ];

    /// <summary>The sources of build-media (downloaded by it when missing).</summary>
    public static IEnumerable<DataSource> Media => new[] { MilkyWayPanorama }.Concat(Landscapes);
}
