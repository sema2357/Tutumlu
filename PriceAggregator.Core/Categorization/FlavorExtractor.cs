using PriceAggregator.Core.Helpers;

namespace PriceAggregator.Core.Categorization;

public static class FlavorExtractor
{
    private static readonly (string CanonicalName, string[] Patterns)[] Flavors =
    [
        ("Hindistan Cevizi & Açai", ["hindistan cevizi ve acai", "hindistan cevizi acai", "kokonat ve acai", "hindistancevizi ve acai"]),
        ("Hindistan Cevizi", ["hindistan cevizi", "hindistancevizi", "kokonat"]),
        ("Açai", ["acai", "açai"]),
        ("Karpuz", ["karpuz", "karpuzlu"]),
        ("Mango", ["mango", "mangolu"]),
        ("Misket Limon (Lime)", ["misket limon", "misket limonu", "lime", "sudachi"]),
        ("Şeftali", ["seftali", "seftalili"]),
        ("Çilek", ["cilek", "cilekli"]),
        ("Muz", ["muz", "muzlu"]),
        ("Portakal", ["portakal", "portakalli"]),
        ("Limon", ["limon", "limonlu"]),
        ("Vanilya", ["vanilya", "vanilyali"]),
        ("Karamel", ["karamel", "karamelli"]),
        ("Çikolata", ["cikolata", "cikolatali", "bitter", "kakao", "kakaolu"]),
        ("Fındık", ["findik", "findikli"]),
        ("Fıstık", ["fistik", "fistikli", "antep fistigi"]),
        ("Badem", ["badem", "bademli"]),
        ("Ceviz", ["ceviz", "cevizli"]),
        ("Ahududu", ["ahududu", "ahududulu", "frambuaz", "frambuazli"]),
        ("Böğürtlen", ["bogurtlen", "bogurtlenli"]),
        ("Vişne", ["visne", "visneli"]),
        ("Kiraz", ["kiraz", "kirazli"]),
        ("Yaban Mersini", ["yaban mersini", "yaban mersinli", "blueberry"]),
        ("Nar", ["nar", "narli"]),
        ("Kivi", ["kivi", "kivili"]),
        ("Elma", ["elma", "elmali"]),
        ("Tropikal Meyve", ["tropikal", "tropical", "ejder meyvesi", "passion fruit"]),
        ("Nane", ["nane", "naneli"]),
        ("Zencefil", ["zencefil", "zencefilli"]),
        ("Tarçın", ["tarcin", "tarcinli"])
    ];

    private static readonly (string CanonicalName, string[] Patterns)[] Dietary =
    [
        ("Şekersiz", ["sekersiz", "sugarfree", "seker ilavesiz"]),
        ("Zero (Sıfır Şeker)", ["zero", "sifir seker"]),
        ("Glutensiz", ["glutensiz", "gluten free"]),
        ("Laktozsuz", ["laktozsuz"]),
        ("Yüksek Protein", ["protein", "proteinli", "high protein"]),
        ("Light", ["light", "yagsiz"]),
        ("Organik", ["organik", "organic"])
    ];

    private static readonly (string CanonicalName, string[] Patterns)[] Editions =
    [
        ("The White Edition", ["white edition"]),
        ("The Blue Edition", ["blue edition"]),
        ("The Yellow Edition", ["yellow edition"]),
        ("The Red Edition", ["red edition"]),
        ("The Green Edition", ["green edition"]),
        ("The Pink Edition", ["pink edition"]),
        ("Summer Edition", ["summer edition"]),
        ("Winter Edition", ["winter edition"]),
        ("Peach Edition", ["peach edition"]),
        ("Lilac Edition", ["lilac edition"])
    ];

    public static List<(string Name, string Value)> Extract(string? title)
    {
        var results = new List<(string Name, string Value)>();
        if (string.IsNullOrWhiteSpace(title)) return results;

        var normalized = TurkishTextHelper.NormalizeToAscii(title).ToLowerInvariant();

        // 1. Editions
        foreach (var (canonical, patterns) in Editions)
        {
            if (patterns.Any(p => normalized.Contains(p)))
            {
                results.Add(("Seri / Edisyon", canonical));
                break;
            }
        }

        // 2. Flavors
        foreach (var (canonical, patterns) in Flavors)
        {
            if (patterns.Any(p => normalized.Contains(p)))
            {
                results.Add(("Aroma", canonical));
                break;
            }
        }

        // 3. Dietary
        foreach (var (canonical, patterns) in Dietary)
        {
            if (patterns.Any(p => normalized.Contains(p)))
            {
                results.Add(("Diyet / Özellik", canonical));
                break;
            }
        }

        return results;
    }
}
