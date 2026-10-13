using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core.Helpers;

namespace PriceAggregator.Api.Helpers;

public static class ProductFilterHelpers
{
    public static string WordBoundaryLike(string column, string paramName = "@Query") => $"(' ' + {column} + ' ') LIKE ('% ' + {paramName} + ' %')";

    public static async Task<(string WhereClause, DynamicParameters Parameters, List<int>? MatchingCatIds)> BuildBaseProductFilterAsync(
        SqlConnection db,
        string? category,
        string? q,
        decimal? minPrice,
        decimal? maxPrice,
        bool? onSale)
    {
        var whereClause = " WHERE EXISTS (SELECT 1 FROM Offers o WHERE o.ProductId = p.Id)";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryTreeIds = (await db.QueryAsync<int>(@"
                WITH CatTree AS (
                    SELECT Id FROM Categories WHERE Slug = @CategorySlug
                    UNION ALL
                    SELECT c.Id FROM Categories c JOIN CatTree ct ON c.ParentCategoryId = ct.Id
                )
                SELECT DISTINCT Id FROM CatTree", new { CategorySlug = category }, commandTimeout: 15)).ToList();

            if (categoryTreeIds.Count > 0)
            {
                whereClause += " AND p.CategoryId IN @CategoryTreeIds";
                parameters.Add("CategoryTreeIds", categoryTreeIds);
            }
            else
            {
                whereClause += " AND 1 = 0";
            }
        }

        List<int>? matchingCatIds = null;

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var catVariants = TurkishTextHelper.GetSearchVariants(term);

            matchingCatIds = (await db.QueryAsync<int>(@"
                WITH CatTree AS (
                    SELECT Id FROM Categories 
                    WHERE Name IN @CatVariants 
                       OR Name COLLATE Turkish_100_CI_AI = @Query COLLATE Turkish_100_CI_AI
                    UNION ALL
                    SELECT c.Id FROM Categories c JOIN CatTree ct ON c.ParentCategoryId = ct.Id
                )
                SELECT DISTINCT Id FROM CatTree", new { CatVariants = catVariants, Query = term }, commandTimeout: 15)).ToList();

            var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var wordConditions = new List<string>();
            for (int i = 0; i < words.Length; i++)
            {
                var wordVariants = TurkishTextHelper.GetSearchVariants(words[i]);
                var variantConditions = new List<string>();
                for (int v = 0; v < wordVariants.Count; v++)
                {
                    var pName = $"@QW_{i}_{v}";
                    parameters.Add($"QW_{i}_{v}", wordVariants[v]);
                    var wordNameMatch = WordBoundaryLike("p.Name", pName);
                    var wordBrandMatch = $"EXISTS (SELECT 1 FROM Brands b{i}_{v} WHERE b{i}_{v}.Id = p.BrandId AND {WordBoundaryLike($"b{i}_{v}.Name", pName)})";
                    variantConditions.Add(wordNameMatch);
                    variantConditions.Add(wordBrandMatch);
                }

                var colParam = $"@QW_Col_{i}";
                parameters.Add($"QW_Col_{i}", words[i]);
                variantConditions.Add($"(' ' + p.Name + ' ') COLLATE Turkish_100_CI_AI LIKE ('% ' + {colParam} + ' %') COLLATE Turkish_100_CI_AI");
                variantConditions.Add($"EXISTS (SELECT 1 FROM Brands b_col{i} WHERE b_col{i}.Id = p.BrandId AND (' ' + b_col{i}.Name + ' ') COLLATE Turkish_100_CI_AI LIKE ('% ' + {colParam} + ' %') COLLATE Turkish_100_CI_AI)");

                wordConditions.Add($"({string.Join(" OR ", variantConditions)})");
            }
            var wordsClause = wordConditions.Count > 0 ? string.Join(" AND ", wordConditions) : "1 = 1";

            if (matchingCatIds.Count > 0)
            {
                whereClause += $" AND (({wordsClause}) OR p.CategoryId IN @MatchingCatIds)";
                parameters.Add("MatchingCatIds", matchingCatIds);
            }
            else
            {
                whereClause += $" AND ({wordsClause})";
            }
            parameters.Add("Query", term);
        }

        if (onSale == true)
        {
            whereClause += " AND EXISTS (SELECT 1 FROM Offers o3 WHERE o3.ProductId = p.Id AND o3.RegularPrice IS NOT NULL AND o3.RegularPrice > o3.Price)";
        }

        if (minPrice.HasValue)
        {
            whereClause += " AND NOT EXISTS (SELECT 1 FROM Offers o4 WHERE o4.ProductId = p.Id AND o4.Price < @MinPrice)";
            parameters.Add("MinPrice", minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            whereClause += " AND EXISTS (SELECT 1 FROM Offers o5 WHERE o5.ProductId = p.Id AND o5.Price <= @MaxPrice)";
            parameters.Add("MaxPrice", maxPrice.Value);
        }

        return (whereClause, parameters, matchingCatIds);
    }

    public static void AppendBrandFilter(string[]? brands, ref string whereClause, DynamicParameters parameters)
    {
        if (brands is not null && brands.Length > 0)
        {
            var validBrands = brands.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToList();
            if (validBrands.Count == 0) return;

            var brandConditions = new List<string>();
            for (int i = 0; i < validBrands.Count; i++)
            {
                var bName = validBrands[i];
                var pName = $"@BrandVal_{i}";
                parameters.Add(pName, bName);
                var variants = TurkishTextHelper.GetSearchVariants(bName);
                var vParam = $"@BrandVars_{i}";
                parameters.Add(vParam, variants);

                brandConditions.Add($"(b.Name IN {vParam} OR b.Name COLLATE Turkish_100_CI_AI = {pName} COLLATE Turkish_100_CI_AI)");
            }

            whereClause += $" AND EXISTS (SELECT 1 FROM Brands b WHERE b.Id = p.BrandId AND ({string.Join(" OR ", brandConditions)}))";
        }
    }

    public static void AppendStoreFilter(string[]? stores, ref string whereClause, DynamicParameters parameters)
    {
        if (stores is not null && stores.Length > 0)
        {
            var validStores = stores.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if (validStores.Count == 0) return;

            whereClause += @" AND EXISTS (
                SELECT 1 FROM Offers os 
                JOIN Sellers s ON s.Id = os.SellerId 
                WHERE os.ProductId = p.Id AND (s.Name IN @StoreFilters OR LOWER(s.Name) IN @StoreFilters))";
            parameters.Add("StoreFilters", validStores);
        }
    }

    public static List<(string Name, List<string> Values)> ParseAttrGroups(string[]? attr)
    {
        if (attr is null || attr.Length == 0) return [];
        return attr
            .Select(a => a.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], parts => parts[1])
            .Select(g => (g.Key, g.ToList()))
            .ToList();
    }

    public static void AppendAttrGroupFilters(List<(string Name, List<string> Values)> groups, ref string whereClause, DynamicParameters parameters)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            whereClause += $@" AND EXISTS (
                SELECT 1 FROM ProductAttributes pa{i}
                WHERE pa{i}.ProductId = p.Id AND pa{i}.AttributeName = @AttrName{i} AND pa{i}.AttributeValue IN @AttrValues{i})";
            parameters.Add($"AttrName{i}", groups[i].Name);
            parameters.Add($"AttrValues{i}", groups[i].Values);
        }
    }
}
