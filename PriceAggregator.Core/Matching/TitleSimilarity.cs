using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core.Helpers;
using PriceAggregator.Core.Pricing;

namespace PriceAggregator.Core.Matching;

public static class TitleSimilarity
{
    private static readonly ConcurrentDictionary<string, byte> DynamicModifierWords = new(
        new[]
        {
            // Flavors / ingredients / fruits
            "cikolatali", "cikolata", "findikli", "findik", "cilekli", "cilek", "muzlu", "muz",
            "portakalli", "portakal", "limonlu", "limon", "vanilyali", "vanilya", "karamelli", "karamel",
            "fistikli", "fistik", "tarcinli", "tarcin", "balli", "bal", "meyveli", "meyve",
            "kayisili", "kayisi", "seftalili", "seftali", "ananasli", "ananas", "nane", "naneli",
            "kakaolu", "kakao", "damla", "bitter", "antep", "bademli", "badem", "cevizli", "ceviz",
            "hindistan", "cevizi", "acai", "karpuz", "karpuzlu", "mango", "mangolu", "misket", "lime",
            "sudachi", "ahududu", "bogurtlen", "visne", "kiraz", "elma", "armut", "kavun", "nar",
            "kivi", "yaban", "mersini", "mersin", "ejder", "tropikal", "passion", "guava", "yuzu",
            "bergamot", "zencefil", "zerdecal", "karanfil", "kokonat", "hindistancevizi", "salep", "boza",
            // Taste / Spice / Roast / Preparation variants
            "acili", "aci", "acisiz", "tatli", "tatlisi",
            "tuzlu", "tuzsuz", "tuz", "aztuzlu", "boltuzlu",
            "kavrulmus", "cig", "haslanmis", "kizartilmis", "firinlanmis", "tutsulenmis",
            "sade", "baharatli", "sarmisakli", "sarimsakli", "kekikli", "feslegenli", "hardalli",
            "corekotlu", "susamli", "sirke", "sirkeli", "mayonezli", "ketcapli",
            // Editions / series / variants
            "edition", "white", "blue", "yellow", "red", "green", "pink", "purple", "sugarfree", "summer", "winter", "spring", "autumn",
            "classic", "original", "extra", "max", "gold", "silver", "black", "peach", "lilac", "ruby",
            // Dietary / format qualifiers
            "sekersiz", "seker", "kafeinsiz", "kafein", "light", "yagli", "yagsiz", "glutensiz", "gluten",
            "laktozsuz", "laktoz", "diyet", "fitness", "protein", "proteinli", "zero", "organik", "organic",
            // Common variant/color markers
            "beyaz", "siyah", "mavi", "kirmizi", "yesil", "sari", "mor", "pembe", "gri", "turuncu", "lacivert",
            // Life-stage / formula qualifiers
            "cocuk", "bebek", "devam", "anne", "yetiskin"
        }.Select(w => KeyValuePair.Create(w, (byte)1)),
        StringComparer.OrdinalIgnoreCase);

    private static bool _modifiersLoadedFromDb;
    private static readonly SemaphoreSlim _loadLock = new(1, 1);

    public static async Task EnsureModifiersLoadedAsync(SqlConnection db)
    {
        if (_modifiersLoadedFromDb) return;
        await _loadLock.WaitAsync();
        try
        {
            if (_modifiersLoadedFromDb) return;
            var values = await db.QueryAsync<string>(
                "SELECT DISTINCT LOWER(AttributeValue) FROM ProductAttributes WHERE LEN(AttributeValue) BETWEEN 3 AND 30");

            foreach (var val in values)
            {
                if (string.IsNullOrWhiteSpace(val)) continue;
                var tokens = TurkishTextHelper.Tokenize(val, filterStopWords: false);
                foreach (var t in tokens)
                {
                    if (t.Length >= 3 && !TurkishTextHelper.IsGenericDescriptor(t))
                        DynamicModifierWords.TryAdd(t, 1);
                }
            }
            _modifiersLoadedFromDb = true;
        }
        catch
        {
            // Baseline words remain active if DB query encounters any issues
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static bool IsUnitOrFiller(string w)
    {
        if (w is "adet" or "pet" or "sise" or "su" or "dogal" or "kaynak" or "sade" or "kutu" or "paket")
            return true;
        if (SizeParser.IsUnit(w))
            return true;
        return TurkishTextHelper.IsFillerWord(w);
    }

    // Numbers embedded in a title are only noise when they're just restating the size/pack
    // count that's already captured structurally (SizeValue/PackQuantity) — a "1" in "1 Kg" or
    // a "4" in "4'lü Paket". Any OTHER number (a shade code, a fat percentage, a formula stage)
    // is real content and must survive tokenization; see Calculate's leftover check, which
    // treats a surviving bare number as automatically distinguishing without needing it named
    // in DistinguishingModifierWords — no curated word list can enumerate every product code.
    private static HashSet<string> SizeRestatementTokens(decimal? size, int packQuantity)
    {
        var tokens = new HashSet<string>();
        if (size is not null)
        {
            AddNumberTokens(tokens, size.Value);
            AddNumberTokens(tokens, size.Value / 1000); // e.g. 750 (Ml) restated as "0.75" (L)
        }
        if (packQuantity > 1)
            tokens.Add(packQuantity.ToString());
        return tokens;
    }

    // A fractional restatement like "0.75" or "1,5" is never one token by the time it reaches
    // here — the punctuation-to-space cleanup in Tokenize splits it into separate integer-look
    // pieces ("0"/"75", "1"/"5") before this is ever consulted. So a non-whole size contributes
    // both halves individually, or "750 Ml" vs "0,75 L" (the exact same bottle) would otherwise
    // leave an unmatched "0"/"75" leftover and get flagged as if it were a different product.
    private static void AddNumberTokens(HashSet<string> tokens, decimal n)
    {
        if (n == Math.Truncate(n))
        {
            tokens.Add(((long)n).ToString());
            return;
        }
        // SizeValue comes from a decimal(18,3) column, so Dapper hands back trailing-zero-padded
        // values (750.000m), and dividing that by 1000 yields "0.750" rather than "0.75" — trim
        // those back to what the title text would actually contain before splitting on '.'.
        var parts = n.ToString(System.Globalization.CultureInfo.InvariantCulture).Split('.');
        tokens.Add(parts[0]);
        tokens.Add(parts[1].TrimEnd('0'));
    }

    private static HashSet<string> Tokenize(string text, string? brand, decimal? size, int packQuantity)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var normalized = TurkishTextHelper.NormalizeToAscii(text);
        var cleaned = new string(normalized.Select(c =>
            char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ').ToArray());

        var brandTokens = string.IsNullOrWhiteSpace(brand)
            ? []
            : TurkishTextHelper.NormalizeToAscii(brand).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var sizeTokens = SizeRestatementTokens(size, packQuantity);

        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.All(char.IsDigit) ? !sizeTokens.Contains(w) : w.Length > 2)
            .Where(w => !brandTokens.Contains(w))
            .Where(w => !IsUnitOrFiller(w))
            .ToHashSet();
    }

    private static readonly HashSet<string> HarmlessAdjectives = new(
        new[] { "dogal", "taze", "lezzetli", "katkisiz", "geleneksel", "kaliteli", "ozel", "yeni", "klasik", "super", "luks", "premium" },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CategoryOrMarketingFillers = new(
        new[]
        {
            "camasir", "deterjan", "deterjani", "bulasik", "yumusatici", "yumusaticisi",
            "icecek", "icecegi", "aromali", "aroma", "soguk", "sicak", "gazsiz", "gazli",
            "rahat", "pratik", "sofra", "karisimi", "toz", "taneli", "ogutulmus",
            "suyu", "sut", "sutu", "icme", "uht", "kefir", "ayran", "kahve", "cay", "bardak", "poset", "poseti",
            "kalem", "kalemi", "tukenmez", "kursun", "filtre", "kagidi", "kagit", "bakim", "bakimi"
        },
        StringComparer.OrdinalIgnoreCase);

    private static bool IsHarmlessAdjective(string w) => HarmlessAdjectives.Contains(w);
    private static bool IsCategoryOrMarketingFiller(string w) => CategoryOrMarketingFillers.Contains(w);

    public static double Calculate(string title1, string title2, string? brand, decimal? size = null, int packQuantity = 1)
    {
        var tokens1 = Tokenize(title1, brand, size, packQuantity);
        var tokens2 = Tokenize(title2, brand, size, packQuantity);

        if (tokens1.Count == 0 && tokens2.Count == 0) return 1.0;
        if (tokens1.Count == 0 || tokens2.Count == 0) return 0.0;

        // Farklı aromalar/varyantlar (örn. Sade vs Hindistan Cevizi & Açai, veya Karpuz vs Şeftali) asla eşleşemez!
        var modifiers1 = tokens1.Where(t => DynamicModifierWords.ContainsKey(t)).ToList();
        var modifiers2 = tokens2.Where(t => DynamicModifierWords.ContainsKey(t)).ToList();
        if (modifiers1.Count > 0 || modifiers2.Count > 0)
        {
            bool hasMismatch = modifiers1.Any(m1 => !modifiers2.Any(m2 => TokensMatch(m1, m2))) ||
                               modifiers2.Any(m2 => !modifiers1.Any(m1 => TokensMatch(m2, m1)));
            if (hasMismatch) return 0.0;
        }

        bool oneWayContainment(HashSet<string> smaller, HashSet<string> larger)
        {
            var leftover = new HashSet<string>(larger, StringComparer.OrdinalIgnoreCase);
            foreach (var s in smaller)
            {
                var match = leftover.FirstOrDefault(l => TokensMatch(s, l));
                if (match is null) return false;
                leftover.Remove(match);
            }
            return !leftover.Any(w =>
                DynamicModifierWords.ContainsKey(w) ||
                w.All(char.IsDigit) ||
                (!TurkishTextHelper.IsFillerWord(w) && !IsUnitOrFiller(w) && !IsHarmlessAdjective(w) && !IsCategoryOrMarketingFiller(w) && !TurkishTextHelper.IsGenericDescriptor(w))
            );
        }

        if (tokens1.Count <= tokens2.Count && oneWayContainment(tokens1, tokens2))
            return 1.0;
        if (tokens2.Count < tokens1.Count && oneWayContainment(tokens2, tokens1))
            return 1.0;

        var remaining = new HashSet<string>(tokens2);
        int matches = 0;

        foreach (var t1 in tokens1)
        {
            var match = remaining.FirstOrDefault(t2 => TokensMatch(t1, t2));
            if (match != null)
            {
                matches++;
                remaining.Remove(match);
            }
        }

        int union = tokens1.Count + tokens2.Count - matches;
        return (double)matches / union;
    }

    private static readonly (string, string)[] SynonymPairs =
    [
        ("organik", "organic"),
        ("limon", "lemon"),
        ("seftali", "peach"),
        ("portakal", "orange"),
        ("siyah", "black"),
        ("beyaz", "white"),
        ("klasik", "classic")
    ];

    public static bool TokensMatch(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        if (TurkishTextHelper.IsStemMatch(a, b)) return true;

        foreach (var (synA, synB) in SynonymPairs)
        {
            if ((string.Equals(a, synA, StringComparison.OrdinalIgnoreCase) && string.Equals(b, synB, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(a, synB, StringComparison.OrdinalIgnoreCase) && string.Equals(b, synA, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        int shorterLen = Math.Min(a.Length, b.Length);
        if (shorterLen >= 5)
        {
            int maxDist = shorterLen <= 7 ? 1 : 2;
            if (LevenshteinDistance(a, b) <= maxDist)
                return true;
        }

        return false;
    }

    public static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }
}
