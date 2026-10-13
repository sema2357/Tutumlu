using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Categorization;

public record CategoryKeywordEntry(
    string RootCategory,
    string SubCategory,
    string LeafCategory,
    int CategoryId,
    List<string> Keywords
);

public static class CategoryKeywordCatalog
{
    private static List<CategoryKeywordEntry>? _cache;
    private static DateTime _cacheTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public static List<CategoryKeywordEntry> ParseFile(string txtFilePath, List<(int Id, string Name, int? ParentCategoryId, int? SectorId)> dbCategories)
    {
        var targets = new List<CategoryKeywordEntry>();
        var lines = File.ReadAllLines(txtFilePath);

        string currentRoot = "";
        string currentSub = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("## • "))
            {
                currentRoot = trimmed.Replace("## • ", "").Trim();
                currentSub = "";
            }
            else if (trimmed.StartsWith("### → "))
            {
                currentSub = trimmed.Replace("### → ", "").Trim();
            }
            else if (trimmed.StartsWith("* **❖ "))
            {
                var match = Regex.Match(trimmed, @"\* \*\*❖ (.*?)\*\*:\s*(.*)");
                if (match.Success)
                {
                    string leafName = match.Groups[1].Value.Trim();
                    string keywordsRaw = match.Groups[2].Value.Trim();

                    var keywords = keywordsRaw.Split(',')
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (keywords.Count == 0)
                    {
                        keywords.Add(leafName);
                    }

                    var dbCat = dbCategories.FirstOrDefault(c => c.Name.Equals(leafName, StringComparison.OrdinalIgnoreCase) && c.ParentCategoryId.HasValue);
                    if (dbCat.Id > 0)
                    {
                        targets.Add(new CategoryKeywordEntry(currentRoot, currentSub, leafName, dbCat.Id, keywords));
                    }
                }
            }
        }

        return targets;
    }

    public static string? FindTxtFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "kategori_urun_listesi.txt");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static async Task<List<CategoryKeywordEntry>> LoadAsync(SqlConnection db)
    {
        if (_cache is not null && DateTime.UtcNow - _cacheTime < CacheDuration)
            return _cache;

        await _cacheLock.WaitAsync();
        try
        {
            if (_cache is not null && DateTime.UtcNow - _cacheTime < CacheDuration)
                return _cache;

            var txtPath = FindTxtFilePath();
            if (txtPath is null)
            {
                Logger.Log("[CategoryKeywordCatalog] UYARI: kategori_urun_listesi.txt bulunamadı, kategorilendirme atlanacak.");
                _cache = new List<CategoryKeywordEntry>();
                _cacheTime = DateTime.UtcNow;
                return _cache;
            }

            var dbCategories = (await db.QueryAsync<(int Id, string Name, int? ParentCategoryId, int? SectorId)>(
                "SELECT Id, Name, ParentCategoryId, SectorId FROM Categories")).ToList();

            _cache = ParseFile(txtPath, dbCategories);
            _cacheTime = DateTime.UtcNow;
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static readonly HashSet<string> WaterExclusionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "gecirmez", "pompa", "pompasi", "matara", "matarasi", "termos", "termosu",
        "yesili", "hoparlor", "isitici", "isiticisi", "sebil", "sebili", "aritma", "filtre"
    };

    public static bool IsTitleRelevant(string productTitle, List<string> categoryKeywords)
    {
        if (string.IsNullOrWhiteSpace(productTitle) || categoryKeywords == null || categoryKeywords.Count == 0)
            return false;

        var titleTokens = Helpers.TurkishTextHelper.Tokenize(productTitle, filterStopWords: false);
        if (titleTokens.Count == 0) return false;

        foreach (var kw in categoryKeywords)
        {
            if (string.IsNullOrWhiteSpace(kw)) continue;

            var kwTokens = Helpers.TurkishTextHelper.Tokenize(kw, filterStopWords: false).ToList();
            if (kwTokens.Count == 0) continue;

            if (kwTokens.Count == 1)
            {
                var word = kwTokens[0];
                if (Helpers.TurkishTextHelper.IsGenericDescriptor(word)) continue;

                if (string.Equals(word, "su", StringComparison.OrdinalIgnoreCase) &&
                    titleTokens.Any(tt => WaterExclusionTokens.Contains(tt)))
                {
                    continue;
                }

                if (titleTokens.Any(tt => Helpers.TurkishTextHelper.IsStemMatch(tt, word)))
                    return true;
            }
            else
            {
                bool matches = true;
                foreach (var w in kwTokens)
                {
                    if (w.Length <= 2 && Helpers.TurkishTextHelper.IsGenericDescriptor(w)) continue;

                    bool wordMatched = titleTokens.Any(tt => Helpers.TurkishTextHelper.IsStemMatch(tt, w));
                    if (!wordMatched)
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches) return true;
            }
        }

        return false;
    }

    public static HashSet<string> Tokenize(string? text) => Helpers.TurkishTextHelper.Tokenize(text);
    public static bool IsTokenStemMatch(string a, string b) => Helpers.TurkishTextHelper.IsStemMatch(a, b);
    public static double ComputeTokenOverlap(HashSet<string> set1, HashSet<string> set2) => Helpers.TurkishTextHelper.ComputeTokenOverlap(set1, set2);

    public static bool IsSourceCompatible(ProductDto product, CategoryKeywordEntry target)
    {
        var sourceLeaf = (product.CategoryPath != null && product.CategoryPath.Count > 0)
            ? product.CategoryPath[^1]
            : product.CategoryName;
        var sourceLeafTokens = Tokenize(sourceLeaf);
        var sourceParentTokens = Tokenize(product.ParentCategoryName);
        var sourceRootTokens = Tokenize(product.RootCategoryName);

        var allSourceTokens = new HashSet<string>(sourceLeafTokens.Concat(sourceParentTokens).Concat(sourceRootTokens), StringComparer.OrdinalIgnoreCase);
        if (product.CategoryPath != null)
        {
            foreach (var seg in product.CategoryPath)
            {
                foreach (var t in Tokenize(seg)) allSourceTokens.Add(t);
            }
        }
        if (allSourceTokens.Count == 0) return true;

        // 1. Direct Leaf Conflict Check against known catalog entries:
        // If the store provides an explicit leaf category that matches a DIFFERENT leaf category in our catalog,
        // it must NOT be assigned to target!
        if (_cache != null && sourceLeafTokens.Count > 0)
        {
            var conflictingEntry = _cache.FirstOrDefault(e =>
                e.CategoryId != target.CategoryId &&
                (string.Equals(e.LeafCategory, sourceLeaf, StringComparison.OrdinalIgnoreCase) ||
                 ComputeTokenOverlap(Tokenize(e.LeafCategory), sourceLeafTokens) >= 0.85));

            if (conflictingEntry != null)
            {
                return false;
            }
        }

        // 2. Dynamic Hierarchy Overlap Check:
        // Generic root tokens ("gida", "mutfak", "urunleri") are NOT enough to establish compatibility!
        // The store tokens MUST overlap with target.LeafCategory, target.SubCategory, or target.Keywords.
        var targetLeafTokens = Tokenize(target.LeafCategory);
        var targetSubTokens = Tokenize(target.SubCategory);
        var targetKwTokens = new HashSet<string>(target.Keywords.SelectMany(Tokenize), StringComparer.OrdinalIgnoreCase);

        var specificTargetTokens = new HashSet<string>(targetLeafTokens.Concat(targetSubTokens).Concat(targetKwTokens), StringComparer.OrdinalIgnoreCase);

        bool hasOverlap = allSourceTokens.Any(st => specificTargetTokens.Any(tt => IsTokenStemMatch(st, tt)));
        return hasOverlap;
    }

    public static int CalculateMatchScore(ProductDto product, CategoryKeywordEntry entry)
    {
        if (string.IsNullOrWhiteSpace(product.Title)) return 0;

        var culture = new System.Globalization.CultureInfo("tr-TR");
        var titleTokens = Tokenize(product.Title);

        var targetLeafTokens = Tokenize(entry.LeafCategory);
        var targetSubTokens = Tokenize(entry.SubCategory);
        var targetRootTokens = Tokenize(entry.RootCategory);
        var targetKwTokens = new HashSet<string>(entry.Keywords.SelectMany(Tokenize), StringComparer.OrdinalIgnoreCase);

        int totalScore = 0;

        var srcLeaf = Tokenize(product.CategoryName);
        var srcParent = Tokenize(product.ParentCategoryName);
        var srcRoot = Tokenize(product.RootCategoryName);

        if (srcLeaf.Count > 0)
        {
            double leafMatch = ComputeTokenOverlap(srcLeaf, targetLeafTokens);
            double kwMatch = ComputeTokenOverlap(srcLeaf, targetKwTokens);
            if (leafMatch > 0) totalScore += (int)(leafMatch * 500);
            else if (kwMatch > 0) totalScore += (int)(kwMatch * 350);
        }

        if (srcParent.Count > 0)
        {
            double subMatch = ComputeTokenOverlap(srcParent, targetSubTokens);
            if (subMatch > 0) totalScore += (int)(subMatch * 200);
        }

        if (srcRoot.Count > 0)
        {
            double rootMatch = ComputeTokenOverlap(srcRoot, targetRootTokens);
            if (rootMatch > 0) totalScore += (int)(rootMatch * 100);
        }

        if (targetLeafTokens.Count > 0 && targetLeafTokens.All(tl => titleTokens.Any(tt => IsTokenStemMatch(tt, tl))))
        {
            totalScore += targetLeafTokens.Count > 1 ? 120 : 60;
        }

        foreach (var kw in entry.Keywords)
        {
            var kwTokens = Tokenize(kw);
            if (kwTokens.Count == 0) continue;

            if (kwTokens.Count == 1 && Helpers.TurkishTextHelper.IsGenericDescriptor(kwTokens.First()))
                continue;

            bool allKwMatched = kwTokens.All(kt => titleTokens.Any(tt => IsTokenStemMatch(tt, kt)));
            if (allKwMatched)
            {
                totalScore += kwTokens.Count > 1 ? 90 + (kwTokens.Count * 10) : 35;
            }
        }

        return totalScore;
    }

    public static int? MatchProduct(ProductDto product, List<CategoryKeywordEntry> catalog)
    {
        int? bestCategoryId = null;
        int bestScore = 0;

        foreach (var entry in catalog)
        {
            if (!IsSourceCompatible(product, entry)) continue;

            int score = CalculateMatchScore(product, entry);
            if (score > bestScore)
            {
                bestScore = score;
                bestCategoryId = entry.CategoryId;
            }
        }

        return bestScore >= 20 ? bestCategoryId : null;
    }
}
