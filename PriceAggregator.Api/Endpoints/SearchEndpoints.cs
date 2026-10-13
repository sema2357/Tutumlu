using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Core;
using StackExchange.Redis;

namespace PriceAggregator.Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/search-suggestions", async (string? q, [FromServices] ElasticsearchService esService) =>
        {
            var term = q?.Trim() ?? "";
            if (term.Length < 2)
                return Results.Ok(new { Brands = Array.Empty<object>(), Products = Array.Empty<object>() });

            var variants = PriceAggregator.Core.Helpers.TurkishTextHelper.GetSearchVariants(term);
            var parameters = new DynamicParameters();
            var catLikes = new List<string>();
            var brandLikes = new List<string>();
            var prodLikes = new List<string>();

            for (int i = 0; i < variants.Count; i++)
            {
                parameters.Add($"Term{i}", $"%{variants[i]}%");
                catLikes.Add($"Name LIKE @Term{i}");
                brandLikes.Add($"b.Name LIKE @Term{i}");
                prodLikes.Add($"p.Name LIKE @Term{i}");
            }
            parameters.Add("TermCol", $"%{term}%");
            catLikes.Add("Name COLLATE Turkish_100_CI_AI LIKE @TermCol COLLATE Turkish_100_CI_AI");
            brandLikes.Add("b.Name COLLATE Turkish_100_CI_AI LIKE @TermCol COLLATE Turkish_100_CI_AI");
            prodLikes.Add("p.Name COLLATE Turkish_100_CI_AI LIKE @TermCol COLLATE Turkish_100_CI_AI");

            var catWhere = string.Join(" OR ", catLikes);
            var brandWhere = string.Join(" OR ", brandLikes);
            var prodWhere = string.Join(" OR ", prodLikes);

            using var db = new SqlConnection(connectionString);

            var categories = await db.QueryAsync($@"
                SELECT TOP 3 Id, Name, Slug, IconClass, IconClass AS IconSlug
                FROM Categories
                WHERE ({catWhere}) AND IsActive = 1
                ORDER BY LEN(Name), Name", parameters);

            if (await esService.IsClusterHealthyAsync())
            {
                var (esBrands, esProducts) = await esService.GetSuggestionsAsync(term);
                if (esBrands.Count > 0 || esProducts.Count > 0 || categories.Any())
                {
                    return Results.Ok(new { Categories = categories, Brands = esBrands, Products = esProducts });
                }
            }

            var brands = await db.QueryAsync<string>($@"
                SELECT TOP 5 b.Name
                FROM Brands b
                WHERE ({brandWhere}) AND EXISTS (SELECT 1 FROM Products p WHERE p.BrandId = b.Id)
                ORDER BY LEN(b.Name), b.Name", parameters);

            var products = await db.QueryAsync($@"
                SELECT TOP 5 p.Id, p.Name AS CanonicalTitle, p.ImageUrl
                FROM Products p
                WHERE ({prodWhere}) AND EXISTS (SELECT 1 FROM Offers o WHERE o.ProductId = p.Id)
                ORDER BY LEN(p.Name), p.Name", parameters);

            return Results.Ok(new { Categories = categories, Brands = brands, Products = products });
        });

        app.MapGet("/api/search", async (
            string? q,
            int? categoryId,
            decimal? minPrice,
            decimal? maxPrice,
            int? page,
            int? pageSize,
            [FromServices] ElasticsearchService esService) =>
        {
            var term = q?.Trim() ?? "";
            int p = page.GetValueOrDefault(1);
            int ps = pageSize.GetValueOrDefault(20);

            if (await esService.IsClusterHealthyAsync())
            {
                var advancedResult = await esService.SearchProductsAdvancedAsync(term, categoryId, minPrice, maxPrice, p, ps);
                if (advancedResult.Products.Count > 0 || advancedResult.BrandFacets.Count > 0)
                {
                    return Results.Ok(advancedResult);
                }
            }

            var fallbackResult = await ExecuteSqlSearchFallbackAsync(connectionString, term, categoryId, minPrice, maxPrice, p, ps);
            return Results.Ok(fallbackResult);
        });

        app.MapGet("/api/popular-searches", async ([FromServices] IConnectionMultiplexer? redis) =>
        {
            var defaultTrends = new[]
            {
                new { Term = "süt", Count = 10 },
                new { Term = "yağ", Count = 8 },
                new { Term = "deterjan", Count = 7 },
                new { Term = "kahve", Count = 5 },
                new { Term = "çay", Count = 4 },
                new { Term = "peynir", Count = 3 }
            };

            if (redis != null && redis.IsConnected)
            {
                try
                {
                    var db = redis.GetDatabase();
                    var keys = Enumerable.Range(0, 7)
                        .Select(i => (RedisKey)$"popular_searches:{DateTime.Now.AddDays(-i):yyyy-MM-dd}")
                        .ToArray();

                    var destKey = (RedisKey)"popular_searches:weekly";
                    var args = new RedisValue[2 + keys.Length];
                    args[0] = destKey.ToString();
                    args[1] = keys.Length;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        args[2 + i] = keys[i].ToString();
                    }
                    await db.ExecuteAsync("ZUNIONSTORE", args);
                    await db.KeyExpireAsync(destKey, TimeSpan.FromMinutes(10));

                    var entries = await db.SortedSetRangeByRankWithScoresAsync(destKey, 0, 9, Order.Descending);

                    if (entries.Length > 0)
                    {
                        var result = entries.Select(e => new
                        {
                            Term = e.Element.ToString(),
                            Count = (int)e.Score
                        }).ToList();

                        return Results.Ok(result);
                    }
                }
                catch
                {
                }
            }

            try
            {
                using var sqlDb = new SqlConnection(connectionString);
                var sqlTrends = (await sqlDb.QueryAsync<(string Term, int Count)>(@"
                    SELECT TOP 10 SearchTerm AS Term, SUM(SearchCount) AS Count
                    FROM DailySearchTrends
                    WHERE SearchDate >= DATEADD(day, -7, GETDATE())
                    GROUP BY SearchTerm
                    ORDER BY SUM(SearchCount) DESC")).ToList();

                if (sqlTrends.Count > 0)
                {
                    return Results.Ok(sqlTrends.Select(t => new { t.Term, t.Count }));
                }
            }
            catch
            {
            }

            return Results.Ok(defaultTrends);
        });

        var historyGroup = app.MapGroup("/api/search-history").RequireAuthorization();

        historyGroup.MapGet("", async (ClaimsPrincipal principal) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            using var db = new SqlConnection(connectionString);
            var terms = await db.QueryAsync<string>(@"
                SELECT SearchTerm
                FROM (
                    SELECT SearchTerm, MAX(CreatedAt) AS LastSearched
                    FROM UserSearchHistory
                    WHERE UserId = @UserId
                    GROUP BY SearchTerm
                ) t
                ORDER BY LastSearched DESC
                OFFSET 0 ROWS FETCH NEXT 8 ROWS ONLY",
                new { UserId = userId.Value });

            return Results.Ok(terms);
        });

        historyGroup.MapDelete("", async (ClaimsPrincipal principal) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            using var db = new SqlConnection(connectionString);
            await db.ExecuteAsync("DELETE FROM UserSearchHistory WHERE UserId = @UserId", new { UserId = userId.Value });

            return Results.Ok();
        });

        historyGroup.MapPost("/log", async (SearchHistoryLogRequest request, ClaimsPrincipal principal) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var term = request.Term?.Trim();
            if (string.IsNullOrWhiteSpace(term)) return Results.Ok();

            using var db = new SqlConnection(connectionString);

            int? productId = request.ProductId;
            int? categoryId = null;

            if (productId is not null)
            {
                categoryId = await db.ExecuteScalarAsync<int?>(
                    "SELECT CategoryId FROM Products WHERE Id = @Id", new { Id = productId.Value });
            }
            else if (!string.IsNullOrWhiteSpace(request.Brand))
            {
                var representative = await db.QuerySingleOrDefaultAsync<(int Id, int? CategoryId)?>(@"
                    SELECT TOP 1 p.Id, p.CategoryId
                    FROM Products p
                    JOIN Brands b ON b.Id = p.BrandId
                    JOIN Offers o ON o.ProductId = p.Id
                    WHERE b.Name = @Brand
                    GROUP BY p.Id, p.CategoryId
                    ORDER BY COUNT(o.Id) DESC",
                    new { request.Brand });

                if (representative is not null)
                {
                    productId = representative.Value.Id;
                    categoryId = representative.Value.CategoryId;
                }
            }

            await db.ExecuteAsync(
                "INSERT INTO UserSearchHistory (UserId, SearchTerm, ProductId, CategoryId) VALUES (@UserId, @SearchTerm, @ProductId, @CategoryId)",
                new { UserId = userId.Value, SearchTerm = term, ProductId = productId, CategoryId = categoryId });

            return Results.Ok();
        });
    }

    private static async Task<AdvancedSearchResultDto> ExecuteSqlSearchFallbackAsync(
        string connectionString,
        string term,
        int? categoryId,
        decimal? minPrice,
        decimal? maxPrice,
        int page,
        int pageSize)
    {
        var result = new AdvancedSearchResultDto();
        if (string.IsNullOrWhiteSpace(term) && !categoryId.HasValue)
            return result;

        try
        {
            using var db = new SqlConnection(connectionString);

            var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var likeParams = new DynamicParameters();
            likeParams.Add("CategoryId", categoryId);
            likeParams.Add("MinPrice", minPrice);
            likeParams.Add("MaxPrice", maxPrice);
            likeParams.Add("Offset", (page - 1) * pageSize);
            likeParams.Add("PageSize", pageSize);

            var whereClauses = new List<string>();
            for (int i = 0; i < words.Length; i++)
            {
                var wordVariants = PriceAggregator.Core.Helpers.TurkishTextHelper.GetSearchVariants(words[i]);
                var variantConditions = new List<string>();
                for (int v = 0; v < wordVariants.Count; v++)
                {
                    var paramName = $"@Term_{i}_{v}";
                    likeParams.Add($"Term_{i}_{v}", $"%{wordVariants[v]}%");
                    variantConditions.Add($"(p.Name LIKE {paramName} OR b.Name LIKE {paramName} OR c.Name LIKE {paramName})");
                }
                var colParam = $"@TermCol_{i}";
                likeParams.Add($"TermCol_{i}", $"%{words[i]}%");
                variantConditions.Add($"(p.Name COLLATE Turkish_100_CI_AI LIKE {colParam} COLLATE Turkish_100_CI_AI OR b.Name COLLATE Turkish_100_CI_AI LIKE {colParam} COLLATE Turkish_100_CI_AI OR c.Name COLLATE Turkish_100_CI_AI LIKE {colParam} COLLATE Turkish_100_CI_AI)");

                whereClauses.Add($"({string.Join(" OR ", variantConditions)})");
            }

            string categoryCte = "";
            if (categoryId.HasValue)
            {
                categoryCte = @";WITH CategoryTree AS (
                    SELECT Id FROM Categories WHERE Id = @CategoryId
                    UNION ALL
                    SELECT c.Id FROM Categories c JOIN CategoryTree ct ON c.ParentCategoryId = ct.Id
                ) ";
                whereClauses.Add("p.CategoryId IN (SELECT Id FROM CategoryTree)");
            }

            var whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            var havingClauses = new List<string>();
            if (minPrice.HasValue) havingClauses.Add("MIN(o.Price) >= @MinPrice");
            if (maxPrice.HasValue) havingClauses.Add("MIN(o.Price) <= @MaxPrice");
            var havingSql = havingClauses.Count > 0 ? "HAVING " + string.Join(" AND ", havingClauses) : "";

            var countSql = $@"{categoryCte}
                SELECT COUNT(*) FROM (
                    SELECT p.Id
                    FROM Products p
                    JOIN Brands b ON b.Id = p.BrandId
                    LEFT JOIN Categories c ON c.Id = p.CategoryId
                    JOIN Offers o ON o.ProductId = p.Id
                    {whereSql}
                    GROUP BY p.Id
                    {havingSql}
                ) t";

            result.TotalCount = await db.ExecuteScalarAsync<long>(countSql, likeParams);

            var querySql = $@"{categoryCte}
                SELECT p.Id, p.Name, b.Id AS BrandId, b.Name AS BrandName,
                       p.CategoryId, c.Name AS CategoryName, c.SectorId,
                       p.ImageUrl, MIN(o.Price) AS LowestPrice,
                       MAX(o.RegularPrice) AS RegularPrice,
                       COUNT(o.Id) AS OfferCount
                FROM Products p
                JOIN Brands b ON b.Id = p.BrandId
                LEFT JOIN Categories c ON c.Id = p.CategoryId
                JOIN Offers o ON o.ProductId = p.Id
                {whereSql}
                GROUP BY p.Id, p.Name, b.Id, b.Name, p.CategoryId, c.Name, c.SectorId, p.ImageUrl
                {havingSql}
                ORDER BY OfferCount DESC, LowestPrice ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var rows = (await db.QueryAsync<ProductIndexModel>(querySql, likeParams)).ToList();
            foreach (var r in rows)
            {
                r.IsOnSale = r.RegularPrice.HasValue && r.RegularPrice > r.LowestPrice;
            }

            result.Products = rows;
            result.ProductIds = rows.Select(p => p.Id).ToList();

            var brandFacetsSql = $@"{categoryCte}
                SELECT TOP 10 b.Name, COUNT(DISTINCT p.Id) AS [Count]
                FROM Products p
                JOIN Brands b ON b.Id = p.BrandId
                LEFT JOIN Categories c ON c.Id = p.CategoryId
                JOIN Offers o ON o.ProductId = p.Id
                {whereSql}
                GROUP BY b.Name
                ORDER BY COUNT(DISTINCT p.Id) DESC";

            var brandFacets = await db.QueryAsync<FacetCountModel>(brandFacetsSql, likeParams);
            var brandMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in brandFacets)
            {
                if (!string.IsNullOrWhiteSpace(b.Name))
                {
                    var titleKey = PriceAggregator.Core.Helpers.TurkishTextHelper.ToTitleCase(b.Name);
                    brandMap[titleKey] = brandMap.GetValueOrDefault(titleKey) + b.Count;
                }
            }
            result.BrandFacets = brandMap
                .Select(kvp => new FacetCountModel { Name = kvp.Key, Count = kvp.Value })
                .OrderByDescending(f => f.Count)
                .Take(10)
                .ToList();

            var categoryFacetsSql = $@"{categoryCte}
                SELECT TOP 10 c.Name, COUNT(DISTINCT p.Id) AS [Count]
                FROM Products p
                JOIN Categories c ON c.Id = p.CategoryId
                JOIN Brands b ON b.Id = p.BrandId
                JOIN Offers o ON o.ProductId = p.Id
                {whereSql}
                GROUP BY c.Name
                ORDER BY COUNT(DISTINCT p.Id) DESC";

            var catFacets = await db.QueryAsync<FacetCountModel>(categoryFacetsSql, likeParams);
            var catMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in catFacets)
            {
                if (!string.IsNullOrWhiteSpace(c.Name))
                {
                    var titleKey = PriceAggregator.Core.Helpers.TurkishTextHelper.ToTitleCase(c.Name);
                    catMap[titleKey] = catMap.GetValueOrDefault(titleKey) + c.Count;
                }
            }
            result.CategoryFacets = catMap
                .Select(kvp => new FacetCountModel { Name = kvp.Key, Count = kvp.Value })
                .OrderByDescending(f => f.Count)
                .Take(10)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchFallback] SQL fallback search error: {ex.Message}");
        }

        return result;
    }
}

public record SearchHistoryLogRequest(string? Term, int? ProductId, string? Brand);
