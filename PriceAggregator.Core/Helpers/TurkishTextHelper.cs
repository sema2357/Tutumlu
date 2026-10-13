using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Helpers;

public static class TurkishTextHelper
{
    public static readonly CultureInfo TurkishCulture = new("tr-TR");

    private static readonly HashSet<string> DefaultFillerTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ve", "veya", "ile", "icin", "reyonu", "kategorisi", "urunleri", "cesitleri", "malzemeleri", "diger", "malzeme"
    };

    private static readonly HashSet<string> GenericDescriptorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "siyah", "beyaz", "mavi", "kirmizi", "yesil", "sari", "mor", "pembe", "gri", "turuncu",
        "ince", "kalin", "kucuk", "buyuk", "dar", "genis", "mini", "maksi", "tekli", "ciftli", "uclu", "dortlu", "besli", "onlu", "set", "yedek",
        "urunleri", "malzemeleri", "aksesuarlari", "cesitleri", "modelleri", "fiyatlari"
    };

    public static bool IsGenericDescriptor(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return true;
        var norm = NormalizeToAscii(token).Trim();
        return GenericDescriptorTokens.Contains(norm);
    }

    public static bool IsFillerWord(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return true;
        var norm = NormalizeToAscii(token).Trim();
        return DefaultFillerTokens.Contains(norm);
    }

    public static string ToTitleCase(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var trimmed = input.Trim();
        var words = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (word.Length == 0) continue;

            if (result.Length > 0) result.Append(' ');

            // If word is an uppercase acronym (e.g., "RGB", "LED", "TV", "USB", "A4"), preserve it
            if (word.Length <= 4 && word.All(c => char.IsUpper(c) || char.IsDigit(c)))
            {
                result.Append(word);
                continue;
            }

            var lower = word.ToLower(TurkishCulture);
            if (lower.Length == 1)
            {
                result.Append(lower.ToUpper(TurkishCulture));
            }
            else
            {
                result.Append(char.ToUpper(lower[0], TurkishCulture));
                result.Append(lower.Substring(1));
            }
        }

        return result.ToString();
    }

    public static string ToLower(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Trim().ToLower(TurkishCulture);
    }

    public static string ToUpper(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Trim().ToUpper(TurkishCulture);
    }

    public static string NormalizeToAscii(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var lower = input.ToLower(TurkishCulture);
        var normalized = lower
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c");

        return Regex.Replace(normalized, @"[^a-z0-9\s-]", " ").Trim();
    }

    public static string GenerateSlug(string? text, string fallback = "kategori")
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        var str = text.Trim();

        str = str.Replace("&", " ve ")
                 .Replace("+", " ve ")
                 .Replace("@", " at ")
                 .Replace("%", " yuzde ");

        str = NormalizeToAscii(str);

        str = Regex.Replace(str, @"[\s-]+", "-").Trim('-');

        return string.IsNullOrWhiteSpace(str) ? fallback : str;
    }

    public static async Task<string> GetUniqueCategorySlugAsync(SqlConnection db, string baseText, int? excludeCategoryId = null)
    {
        var slug = GenerateSlug(baseText);
        var candidate = slug;
        int counter = 1;

        while (true)
        {
            var exists = await db.ExecuteScalarAsync<bool>(@"
                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM Categories 
                    WHERE Slug = @Slug AND (@ExcludeId IS NULL OR Id <> @ExcludeId)
                ) THEN 1 ELSE 0 END AS BIT)",
                new { Slug = candidate, ExcludeId = excludeCategoryId });

            if (!exists) return candidate;

            counter++;
            candidate = $"{slug}-{counter}";
        }
    }

    public static HashSet<string> Tokenize(string? text, bool filterStopWords = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var normalized = NormalizeToAscii(text);
        var words = Regex.Split(normalized, @"[^a-z0-9]+")
            .Where(w => w.Length >= 2 && (!filterStopWords || !DefaultFillerTokens.Contains(w)));

        return new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
    }

    public static string GetCanonicalStem(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return string.Empty;
        var ascii = NormalizeToAscii(word).Trim();
        if (ascii.Length <= 1) return ascii;

        // Specific high-frequency core roots in retail taxonomy
        if (ascii.StartsWith("su") && (ascii == "su" || ascii == "suyu" || ascii == "suyun" || ascii == "suyunu" || ascii == "sular" || ascii == "sulari" || ascii == "suluk" || ascii == "sulu"))
            return "su";
        if (ascii.StartsWith("cay") && (ascii == "cay" || ascii == "cayi" || ascii == "caylar" || ascii == "caylari" || ascii == "caylik"))
            return "cay";
        if (ascii.StartsWith("et") && (ascii == "et" || ascii == "eti" || ascii == "etler" || ascii == "etleri"))
            return "et";
        if (ascii.StartsWith("un") && (ascii == "un" || ascii == "unu" || ascii == "unlar" || ascii == "unlari"))
            return "un";
        if (ascii.StartsWith("ot") && (ascii == "ot" || ascii == "otu" || ascii == "otlar" || ascii == "otlari"))
            return "ot";
        if (ascii.StartsWith("pil") && (ascii == "pil" || ascii == "pili" || ascii == "piller" || ascii == "pilleri"))
            return "pil";
        if (ascii.StartsWith("bal") && (ascii == "bal" || ascii == "bali" || ascii == "ballar" || ascii == "balli"))
            return "bal";
        if (ascii.StartsWith("sut") && (ascii == "sut" || ascii == "sutu" || ascii == "sutler" || ascii == "sutlu"))
            return "sut";
        if (ascii.StartsWith("yag") && (ascii == "yag" || ascii == "yagi" || ascii == "yaglar" || ascii == "yaglari" || ascii == "yagli"))
            return "yag";
        if (ascii.StartsWith("tuz") && (ascii == "tuz" || ascii == "tuzu" || ascii == "tuzlar" || ascii == "tuzlu"))
            return "tuz";

        // General Turkish morphological stripping:
        // Strip plural possessive: -lari, -leri (e.g. sulari -> su, meyveleri -> meyve)
        if ((ascii.EndsWith("lari") || ascii.EndsWith("leri")) && ascii.Length >= 5)
        {
            var s = ascii[..^4];
            if (s.Length >= 2) return s;
        }

        // Strip plural: -lar, -ler (e.g. sular -> su, caylar -> cay, meyveler -> meyve)
        if ((ascii.EndsWith("lar") || ascii.EndsWith("ler")) && ascii.Length >= 4)
        {
            var s = ascii[..^3];
            if (s.Length >= 2) return s;
        }

        // Strip attributive: -lik, -lik, -luk, -luk (e.g. atistirmalik -> atistir, caylik -> cay)
        if ((ascii.EndsWith("lik") || ascii.EndsWith("luk")) && ascii.Length >= 5)
        {
            var s = ascii[..^3];
            if (s.Length >= 3) return s;
        }

        // Strip possessive with buffer: -si, -su, -si, -su (e.g. meyvesi -> meyve, salcasi -> salca)
        if ((ascii.EndsWith("si") || ascii.EndsWith("su")) && ascii.Length >= 5)
        {
            var s = ascii[..^2];
            if (s.Length >= 3) return s;
        }

        // Strip flavor/attribute suffix: -li, -lu, -li, -lu (e.g. meyveli -> meyve, cikolatali -> cikolata, sutlu -> sut)
        if ((ascii.EndsWith("li") || ascii.EndsWith("lu")) && ascii.Length >= 4)
        {
            var s = ascii[..^2];
            if (s.Length >= 2) return s;
        }

        // Strip simple possessive/accusative: -i, -u (e.g. cayi -> cay, zeytini -> zeytin, peyniri -> peynir, surubu -> surub)
        if ((ascii.EndsWith("i") || ascii.EndsWith("u")) && ascii.Length >= 4)
        {
            var s = ascii[..^1];
            if (s.Length >= 3) ascii = s;
        }

        // Turkish Consonant Hardening (Reverting ünsüz yumuşaması on stem boundary):
        // In Turkish morphology, root-final unvoiced stops (p, ç, t, k) soften to (b, c, d, g/ğ) before vowel suffixes.
        // When those suffixes are removed, normalize the final consonant back to the canonical unvoiced stop:
        // b -> p (surub -> surup, dolab -> dolap, kitab -> kitap)
        // d -> t (kagid -> kagit, yogurd -> yogurt, simid -> simit)
        // g -> k (findig -> findik, iceceg -> icecek, bardag -> bardak)
        return HardenFinalConsonant(ascii);
    }

    public static string HardenFinalConsonant(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem) || stem.Length < 3) return stem;
        if (stem.EndsWith("b")) return stem[..^1] + "p";
        if (stem.EndsWith("d")) return stem[..^1] + "t";
        if (stem.EndsWith("g")) return stem[..^1] + "k";
        return stem;
    }

    private static readonly HashSet<string> UnitAndMeasurementTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "gr", "g", "gram", "kg", "kilogram", "kilo",
        "ml", "mililitre", "l", "lt", "litre", "cl", "cc",
        "cm", "mm", "m", "metre", "in", "inc",
        "adet", "ad", "adetli", "pk", "paket", "koli",
        "lu", "li", "luk", "lik",
        "w", "watt", "v", "volt", "mah", "ah", "gb", "mb", "tb"
    };

    public static string NormalizeInflections(string word) => GetCanonicalStem(word);

    public static string? GetHeadNoun(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return null;
        var tokens = Tokenize(phrase, filterStopWords: false).ToList();
        if (tokens.Count == 0) return null;

        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (string.IsNullOrWhiteSpace(t)) continue;

            var norm = NormalizeToAscii(t);
            // Skip pure numbers or dimensions (e.g. 21x13, 100, 250)
            if (Regex.IsMatch(norm, @"^\d+([xX*]\d+)*$")) continue;

            // Skip units and measurements
            if (UnitAndMeasurementTokens.Contains(norm)) continue;

            // Skip combined digit+unit (e.g. 250g, 1lt, 21x13cm)
            if (Regex.IsMatch(norm, @"^\d+[a-z]+$")) continue;

            return GetCanonicalStem(norm);
        }

        return GetCanonicalStem(tokens[^1]);
    }


    public static bool HasPolarityConflict(string tokenA, string tokenB)
    {
        var normA = NormalizeToAscii(tokenA);
        var normB = NormalizeToAscii(tokenB);

        // Check if one has privative suffix (-siz, -suz) and the other has attributive suffix (-li, -lu) or bare root
        bool aIsPrivative = normA.EndsWith("siz") || normA.EndsWith("suz");
        bool bIsPrivative = normB.EndsWith("siz") || normB.EndsWith("suz");

        bool aIsAttributive = normA.EndsWith("li") || normA.EndsWith("lu");
        bool bIsAttributive = normB.EndsWith("li") || normB.EndsWith("lu");

        if ((aIsPrivative && bIsAttributive) || (aIsAttributive && bIsPrivative))
        {
            var rootA = aIsPrivative ? normA[..^3] : (aIsAttributive ? normA[..^2] : normA);
            var rootB = bIsPrivative ? normB[..^3] : (bIsAttributive ? normB[..^2] : normB);

            if (string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetCanonicalStem(rootA), GetCanonicalStem(rootB), StringComparison.OrdinalIgnoreCase))
            {
                return true; // e.g. "gazsiz" vs "gazli", "sekersiz" vs "sekerli" -> Antonym Conflict!
            }
        }

        // Privative vs bare root (e.g., "acisiz" vs "aci", "sekersiz" vs "seker", "tuzsuz" vs "tuz")
        if (aIsPrivative != bIsPrivative)
        {
            var privativeToken = aIsPrivative ? normA : normB;
            var bareToken = aIsPrivative ? normB : normA;
            var privativeRoot = privativeToken[..^3];

            if (string.Equals(privativeRoot, bareToken, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetCanonicalStem(privativeRoot), GetCanonicalStem(bareToken), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsStemMatch(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        if (HasPolarityConflict(a, b)) return false;

        var normA = NormalizeToAscii(a);
        var normB = NormalizeToAscii(b);

        if (string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase)) return true;

        var stemA = GetCanonicalStem(normA);
        var stemB = GetCanonicalStem(normB);

        if (string.Equals(stemA, stemB, StringComparison.OrdinalIgnoreCase)) return true;

        var hardA = HardenFinalConsonant(stemA);
        var hardB = HardenFinalConsonant(stemB);

        return string.Equals(hardA, hardB, StringComparison.OrdinalIgnoreCase);
    }

    public static double ComputeTokenOverlap(HashSet<string> set1, HashSet<string> set2)
    {
        if (set1.Count == 0 || set2.Count == 0) return 0.0;

        // Direct grammatical polarity conflict (e.g. gazsiz vs gazli) immediately disqualifies overlap
        foreach (var s1 in set1)
        {
            if (set2.Any(s2 => HasPolarityConflict(s1, s2)))
                return 0.0;
        }

        int matches = 0;
        foreach (var s1 in set1)
        {
            if (set2.Any(s2 => IsStemMatch(s1, s2)))
                matches++;
        }
        return (double)matches / Math.Max(set1.Count, set2.Count);
    }

    public static List<string> GetSearchVariants(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        var str = input.Trim();
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            str,
            ToLower(str),
            ToUpper(str),
            ToTitleCase(str),
            str.ToLowerInvariant(),
            str.ToUpperInvariant()
        };

        var ascii = NormalizeToAscii(str);
        if (!string.IsNullOrEmpty(ascii))
        {
            variants.Add(ascii);
            variants.Add(ToTitleCase(ascii));
            variants.Add(ascii.ToUpperInvariant());
            variants.Add(ascii.ToLowerInvariant());
        }

        var candidates = new List<StringBuilder> { new() };
        var lowerStr = str.ToLower(TurkishCulture);

        foreach (var ch in lowerStr)
        {
            char[] options = ch switch
            {
                'c' => ['c', 'ç'],
                'ç' => ['ç', 'c'],
                's' => ['s', 'ş'],
                'ş' => ['ş', 's'],
                'g' => ['g', 'ğ'],
                'ğ' => ['ğ', 'g'],
                'u' => ['u', 'ü'],
                'ü' => ['ü', 'u'],
                'o' => ['o', 'ö'],
                'ö' => ['ö', 'o'],
                'i' => ['i', 'ı'],
                'ı' => ['ı', 'i'],
                _ => [ch]
            };

            if (options.Length == 1)
            {
                foreach (var sb in candidates) sb.Append(options[0]);
            }
            else
            {
                var newCandidates = new List<StringBuilder>(candidates.Count * options.Length);
                foreach (var sb in candidates)
                {
                    foreach (var opt in options)
                    {
                        var copy = new StringBuilder(sb.ToString()).Append(opt);
                        newCandidates.Add(copy);
                    }
                }
                candidates = newCandidates.Count > 32 ? newCandidates.Take(32).ToList() : newCandidates;
            }
        }

        foreach (var c in candidates)
        {
            var text = c.ToString();
            variants.Add(text);
            variants.Add(ToLower(text));
            variants.Add(ToTitleCase(text));
            variants.Add(ToUpper(text));
        }

        return variants.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
    }

    public static bool EqualsTurkish(string? a, string? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return TurkishCulture.CompareInfo.Compare(a.Trim(), b.Trim(), CompareOptions.IgnoreCase) == 0;
    }
}
