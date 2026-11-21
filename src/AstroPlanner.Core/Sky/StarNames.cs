using System.Globalization;

namespace AstroPlanner.Core.Sky;

/// <summary>
/// Names of stars from their HYG identifiers: the Bayer letter ("Alp", "Bet-1") as Greek ("α",
/// "β¹") or spelled out ("Alpha", "Beta"), the Flamsteed number, and the Latin genitive of the
/// constellation for names like "Beta Cygni" (the form Wikipedia and atlases use).
/// </summary>
public static class StarNames
{
    private static readonly Dictionary<string, (string Letter, string Spelled)> Greek = new(StringComparer.Ordinal)
    {
        ["Alp"] = ("α", "Alpha"), ["Bet"] = ("β", "Beta"), ["Gam"] = ("γ", "Gamma"), ["Del"] = ("δ", "Delta"),
        ["Eps"] = ("ε", "Epsilon"), ["Zet"] = ("ζ", "Zeta"), ["Eta"] = ("η", "Eta"), ["The"] = ("θ", "Theta"),
        ["Iot"] = ("ι", "Iota"), ["Kap"] = ("κ", "Kappa"), ["Lam"] = ("λ", "Lambda"), ["Mu"] = ("μ", "Mu"),
        ["Nu"] = ("ν", "Nu"), ["Xi"] = ("ξ", "Xi"), ["Omi"] = ("ο", "Omicron"), ["Pi"] = ("π", "Pi"),
        ["Rho"] = ("ρ", "Rho"), ["Sig"] = ("σ", "Sigma"), ["Tau"] = ("τ", "Tau"), ["Ups"] = ("υ", "Upsilon"),
        ["Phi"] = ("φ", "Phi"), ["Chi"] = ("χ", "Chi"), ["Psi"] = ("ψ", "Psi"), ["Ome"] = ("ω", "Omega")
    };

    /// <summary>Latin genitive of each IAU constellation, by abbreviation.</summary>
    private static readonly Dictionary<string, string> Genitives = new(StringComparer.Ordinal)
    {
        ["And"] = "Andromedae", ["Ant"] = "Antliae", ["Aps"] = "Apodis", ["Aqr"] = "Aquarii", ["Aql"] = "Aquilae",
        ["Ara"] = "Arae", ["Ari"] = "Arietis", ["Aur"] = "Aurigae", ["Boo"] = "Boötis", ["Cae"] = "Caeli",
        ["Cam"] = "Camelopardalis", ["Cnc"] = "Cancri", ["CVn"] = "Canum Venaticorum", ["CMa"] = "Canis Majoris",
        ["CMi"] = "Canis Minoris", ["Cap"] = "Capricorni", ["Car"] = "Carinae", ["Cas"] = "Cassiopeiae", ["Cen"] = "Centauri",
        ["Cep"] = "Cephei", ["Cet"] = "Ceti", ["Cha"] = "Chamaeleontis", ["Cir"] = "Circini", ["Col"] = "Columbae",
        ["Com"] = "Comae Berenices", ["CrA"] = "Coronae Australis", ["CrB"] = "Coronae Borealis", ["Crv"] = "Corvi",
        ["Crt"] = "Crateris", ["Cru"] = "Crucis", ["Cyg"] = "Cygni", ["Del"] = "Delphini", ["Dor"] = "Doradus",
        ["Dra"] = "Draconis", ["Equ"] = "Equulei", ["Eri"] = "Eridani", ["For"] = "Fornacis", ["Gem"] = "Geminorum",
        ["Gru"] = "Gruis", ["Her"] = "Herculis", ["Hor"] = "Horologii", ["Hya"] = "Hydrae", ["Hyi"] = "Hydri",
        ["Ind"] = "Indi", ["Lac"] = "Lacertae", ["Leo"] = "Leonis", ["LMi"] = "Leonis Minoris", ["Lep"] = "Leporis",
        ["Lib"] = "Librae", ["Lup"] = "Lupi", ["Lyn"] = "Lyncis", ["Lyr"] = "Lyrae", ["Men"] = "Mensae",
        ["Mic"] = "Microscopii", ["Mon"] = "Monocerotis", ["Mus"] = "Muscae", ["Nor"] = "Normae", ["Oct"] = "Octantis",
        ["Oph"] = "Ophiuchi", ["Ori"] = "Orionis", ["Pav"] = "Pavonis", ["Peg"] = "Pegasi", ["Per"] = "Persei",
        ["Phe"] = "Phoenicis", ["Pic"] = "Pictoris", ["Psc"] = "Piscium", ["PsA"] = "Piscis Austrini", ["Pup"] = "Puppis",
        ["Pyx"] = "Pyxidis", ["Ret"] = "Reticuli", ["Sge"] = "Sagittae", ["Sgr"] = "Sagittarii", ["Sco"] = "Scorpii",
        ["Scl"] = "Sculptoris", ["Sct"] = "Scuti", ["Ser"] = "Serpentis", ["Sex"] = "Sextantis", ["Tau"] = "Tauri",
        ["Tel"] = "Telescopii", ["Tri"] = "Trianguli", ["TrA"] = "Trianguli Australis", ["Tuc"] = "Tucanae",
        ["UMa"] = "Ursae Majoris", ["UMi"] = "Ursae Minoris", ["Vel"] = "Velorum", ["Vir"] = "Virginis",
        ["Vol"] = "Volantis", ["Vul"] = "Vulpeculae"
    };

    /// <summary>"Sirius", "α CMa", "β¹ Cyg", "61 Cyg" or null: the label the sky and the tables show.</summary>
    public static string? Label(string? properName, string? bayer, int? flamsteed, string constellation)
    {
        if (!string.IsNullOrWhiteSpace(properName))
        {
            return properName.Trim();
        }

        return GreekBayer(bayer, constellation) ?? Flamsteed(flamsteed, constellation);
    }

    /// <summary>"α CMa", "β¹ Cyg" (or "β Cyg" without <paramref name="withIndex"/>), or null without a Bayer letter.</summary>
    public static string? GreekBayer(string? bayer, string constellation, bool withIndex = true)
    {
        if (string.IsNullOrWhiteSpace(bayer))
        {
            return null;
        }

        var (letter, index) = Split(bayer);
        var greek = Greek.TryGetValue(letter, out var known) ? known.Letter : letter;
        return $"{greek}{(withIndex ? Superscript(index) : string.Empty)} {constellation}";
    }

    /// <summary>"Alpha Canis Majoris", "Beta1 Cygni" (or "Beta Cygni" without <paramref name="withIndex"/>), or null.</summary>
    public static string? SpelledBayer(string? bayer, string constellation, bool withIndex = true)
    {
        if (string.IsNullOrWhiteSpace(bayer) || !Genitives.TryGetValue(constellation, out var genitive))
        {
            return null;
        }

        var (letter, index) = Split(bayer);
        return Greek.TryGetValue(letter, out var known) ? $"{known.Spelled}{(withIndex ? index : string.Empty)} {genitive}" : null;
    }

    /// <summary>"61 Cyg", or null.</summary>
    public static string? Flamsteed(int? flamsteed, string constellation) =>
        flamsteed is { } number ? string.Create(CultureInfo.InvariantCulture, $"{number} {constellation}") : null;

    /// <summary>"61 Cygni", or null.</summary>
    public static string? SpelledFlamsteed(int? flamsteed, string constellation) =>
        flamsteed is { } number && Genitives.TryGetValue(constellation, out var genitive)
            ? string.Create(CultureInfo.InvariantCulture, $"{number} {genitive}")
            : null;

    /// <summary>Latin genitive of a constellation abbreviation ("Cyg" → "Cygni"), or null.</summary>
    public static string? Genitive(string abbreviation) => Genitives.GetValueOrDefault(abbreviation);

    private static (string Letter, string Index) Split(string bayer)
    {
        var parts = bayer.Trim().Split('-');
        return (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static string Superscript(string digits) =>
        string.Concat(digits.Select(c => c switch
        {
            '1' => '¹', '2' => '²', '3' => '³', >= '0' and <= '9' => (char)('⁰' + (c - '0')), _ => c
        }));
}
