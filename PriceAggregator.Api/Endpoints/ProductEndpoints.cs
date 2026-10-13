using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;
using PriceAggregator.Core;
using StackExchange.Redis;

namespace PriceAggregator.Api.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/products/{productId}", async (int productId) =>
        {
            using var db = new SqlConnection(connectionString);

            var product = await db.QuerySingleOrDefaultAsync<ProductDetailRow>(@"
                SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand, p.ImageUrl,
                       p.SizeValue, p.SizeUnit, p.PackQuantity, CAST(NULL AS NVARCHAR(100)) AS Sku, p.CategoryId
                FROM Products p
                LEFT JOIN Brands b ON b.Id = p.BrandId
                WHERE p.Id = @ProductId",
                new { ProductId = productId });

            if (product is null) return Results.NotFound();

            var categoryPath = product.CategoryId is null
                ? new List<string>()
                : (await db.QueryAsync<string>(@"
                    ;WITH Ancestors AS (
                        SELECT Id, Name, ParentCategoryId, 0 AS Depth FROM Categories WHERE Id = @CategoryId
                        UNION ALL
                        SELECT c.Id, c.Name, c.ParentCategoryId, a.Depth + 1
                        FROM Categories c JOIN Ancestors a ON c.Id = a.ParentCategoryId
                    )
                    SELECT Name FROM Ancestors ORDER BY Depth DESC",
                    new { product.CategoryId })).ToList();

            var offers = await db.QueryAsync<OfferRow>(@"
                SELECT o.SellerId, p.Name AS CanonicalTitle, s.Name AS Source, o.Price, o.RegularPrice,
                       sp.ExternalImageUrl AS ImageUrl, sp.ExternalUrl AS ProductUrl,
                       s.Name AS StoreName,
                       sp.ExternalTitle,
                       p.SizeValue, p.SizeUnit, p.PackQuantity
                FROM Offers o
                JOIN Products p ON p.Id = o.ProductId
                JOIN Sellers s ON s.Id = o.SellerId
                JOIN SellerProducts sp ON sp.Id = o.SellerProductId
                WHERE o.ProductId = @ProductId
                ORDER BY o.Price ASC",
                new { ProductId = productId });

            var offerResult = offers.Select(o =>
            {
                var unitPrice = UnitPriceCalculator.Calculate(o.Price, o.SizeValue, o.SizeUnit, o.PackQuantity);
                var discountPercent = o.RegularPrice.HasValue && o.RegularPrice > o.Price
                    ? Math.Round((o.RegularPrice.Value - o.Price) / o.RegularPrice.Value * 100, 0)
                    : (decimal?)null;
                return new
                {
                    o.SellerId,
                    o.CanonicalTitle,
                    o.Source,
                    o.Price,
                    o.ImageUrl,
                    o.ProductUrl,
                    StoreName = FormatStoreName(o.StoreName),
                    o.ExternalTitle,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label,
                    OldPrice = discountPercent.HasValue ? o.RegularPrice : null,
                    DiscountPercent = discountPercent
                };
            });

            var attributeRows = await db.QueryAsync<ProductAttributeRow>(@"
                SELECT ProductId, AttributeName, AttributeValue
                FROM ProductAttributes
                WHERE ProductId = @ProductId AND AttributeName <> 'Adet'
                ORDER BY AttributeName, AttributeValue",
                new { ProductId = productId });

            var attributes = attributeRows
                .GroupBy(a => a.AttributeName)
                .Select(g => new { Name = g.Key, Values = g.Select(a => a.AttributeValue).Distinct().ToList() })
                .ToList();

            IEnumerable<ProductSpecRow> specRows = Enumerable.Empty<ProductSpecRow>();
            try
            {
                specRows = await db.QueryAsync<ProductSpecRow>(@"
                    SELECT SpecGroup, SpecName, SpecValue
                    FROM ProductSpecs
                    WHERE ProductId = @ProductId",
                    new { ProductId = productId });
            }
            catch
            {
            }

            var specs = specRows
                .GroupBy(s => s.SpecGroup)
                .Select(g => new { Group = g.Key, Items = g.Select(s => new { s.SpecName, s.SpecValue }).ToList() })
                .ToList();

            return Results.Ok(new
            {
                Product = new
                {
                    product.Id,
                    product.CanonicalTitle,
                    product.Brand,
                    product.ImageUrl,
                    product.SizeValue,
                    product.SizeUnit,
                    product.PackQuantity,
                    product.Sku,
                    CategoryPath = categoryPath,
                    Attributes = attributes,
                    Specs = specs
                },
                Offers = offerResult
            });
        });

        app.MapGet("/api/products/{productId}/similar", async (int productId, ICacheService cache) =>
        {
            var cacheKey = $"similar_products_{productId}";
            var result = await cache.GetOrSetAsync<List<SimilarProductRow>>(cacheKey, TimeSpan.FromHours(1), async () =>
            {
                using var db = new SqlConnection(connectionString);

                var target = await db.QuerySingleOrDefaultAsync<TargetProductRow>(@"
                    SELECT p.Name, p.CategoryId, p.BrandId
                    FROM Products p
                    WHERE p.Id = @Id",
                    new { Id = productId });

                if (target is null) return new List<SimilarProductRow>();

                var scored = Enumerable.Empty<ScoredProduct>();
                if (target.CategoryId is not null)
                {
                    var scopeCategoryId = await db.QuerySingleOrDefaultAsync<int?>(@"
                        ;WITH Ancestors AS (
                            SELECT Id, ParentCategoryId, SectorId, 0 AS Depth FROM Categories WHERE Id = @CategoryId
                            UNION ALL
                            SELECT c.Id, c.ParentCategoryId, c.SectorId, a.Depth + 1
                            FROM Categories c JOIN Ancestors a ON c.Id = a.ParentCategoryId
                        )
                        SELECT TOP 1 Id FROM Ancestors WHERE ParentCategoryId = SectorId ORDER BY Depth ASC",
                        new { target.CategoryId }) ?? target.CategoryId.Value;

                    var candidates = await db.QueryAsync<ProductListRow>(@"
                        WITH CategoryTree AS (
                            SELECT Id FROM Categories WHERE Id = @ScopeCategoryId
                            UNION ALL
                            SELECT c.Id FROM Categories c JOIN CategoryTree ct ON c.ParentCategoryId = ct.Id
                        )
                        SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                               MIN(o.Price) AS LowestPrice,
                               COUNT(o.Id) AS OfferCount,
                               p.ImageUrl,
                               p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                               (SELECT TOP 1 o2.RegularPrice FROM Offers o2 WHERE o2.ProductId = p.Id ORDER BY o2.Price ASC) AS OldPrice,
                               (SELECT STRING_AGG(sName, ', ') FROM (SELECT DISTINCT s.Name AS sName FROM Offers o3 JOIN Sellers s ON s.Id = o3.SellerId WHERE o3.ProductId = p.Id AND o3.Price = (SELECT MIN(Price) FROM Offers WHERE ProductId = p.Id)) d) AS LowestPriceStore
                        FROM Products p
                        LEFT JOIN Brands b ON b.Id = p.BrandId
                        JOIN Offers o ON o.ProductId = p.Id
                        WHERE p.Id <> @ProductId
                        AND p.CategoryId IN (SELECT Id FROM CategoryTree)
                        GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId",
                        new { ProductId = productId, ScopeCategoryId = scopeCategoryId });

                    await TitleSimilarity.EnsureModifiersLoadedAsync(db);
                    scored = candidates.Select(c => new ScoredProduct(
                        c, TitleSimilarity.Calculate(c.CanonicalTitle, target.Name, c.Brand), c.CategoryId == target.CategoryId));
                }

                const double SiblingSimilarityThreshold = 0.35;
                var eligible = scored
                    .Where(x => x.SameCategory || x.Score >= SiblingSimilarityThreshold)
                    .ToList();

                if (eligible.Count == 0)
                {
                    eligible = scored.ToList();
                }

                if (eligible.Count == 0 && target.BrandId is not null)
                {
                    var brandCandidates = await db.QueryAsync<ProductListRow>(@"
                        SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                               MIN(o.Price) AS LowestPrice,
                               COUNT(o.Id) AS OfferCount,
                               p.ImageUrl,
                               p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                               (SELECT TOP 1 o2.RegularPrice FROM Offers o2 WHERE o2.ProductId = p.Id ORDER BY o2.Price ASC) AS OldPrice,
                               (SELECT STRING_AGG(sName, ', ') FROM (SELECT DISTINCT s.Name AS sName FROM Offers o3 JOIN Sellers s ON s.Id = o3.SellerId WHERE o3.ProductId = p.Id AND o3.Price = (SELECT MIN(Price) FROM Offers WHERE ProductId = p.Id)) d) AS LowestPriceStore
                        FROM Products p
                        LEFT JOIN Brands b ON b.Id = p.BrandId
                        JOIN Offers o ON o.ProductId = p.Id
                        WHERE p.Id <> @ProductId AND p.BrandId = @BrandId
                        GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId
                        ORDER BY COUNT(o.Id) DESC",
                        new { ProductId = productId, target.BrandId });

                    eligible = brandCandidates
                        .Select(c => new ScoredProduct(c, 0, c.CategoryId == target.CategoryId))
                        .ToList();
                }

                return eligible
                    .OrderByDescending(x => x.SameCategory)
                    .ThenByDescending(x => x.Score)
                    .Take(10)
                    .Select(x =>
                    {
                        var unitPrice = UnitPriceCalculator.Calculate(x.Product.LowestPrice, x.Product.SizeValue, x.Product.SizeUnit, x.Product.PackQuantity);
                        return new SimilarProductRow(
                            x.Product.Id,
                            x.Product.CanonicalTitle,
                            x.Product.Brand,
                            x.Product.LowestPrice,
                            x.Product.OfferCount,
                            x.Product.ImageUrl,
                            x.SameCategory,
                            unitPrice?.Amount,
                            unitPrice?.Label,
                            FormatStoreName(x.Product.LowestPriceStore)
                        );
                    })
                    .ToList();
            });

            if (result is null || result.Count == 0) return Results.NotFound();
            return Results.Ok(result);
        });

        app.MapPost("/api/products/{productId}/view", async (int productId, ClaimsPrincipal principal) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            using var db = new SqlConnection(connectionString);
            await db.ExecuteAsync(
                "INSERT INTO UserProductViews (UserId, ProductId) VALUES (@UserId, @ProductId)",
                new { UserId = userId.Value, ProductId = productId });

            return Results.Ok();
        }).RequireAuthorization();

        app.MapGet("/api/products/compare", async (string? ids) =>
        {
            var idList = (ids ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .Take(4)
                .ToList();

            if (idList.Count == 0) return Results.Ok(Array.Empty<object>());

            using var db = new SqlConnection(connectionString);

            var products = await db.QueryAsync<ProductListRow>(@"
                SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                       MIN(o.Price) AS LowestPrice,
                       COUNT(o.Id) AS OfferCount,
                       p.ImageUrl,
                       p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                       MAX(o.RegularPrice) AS OldPrice,
                       CAST(NULL AS nvarchar(200)) AS LowestPriceStore
                FROM Products p
                LEFT JOIN Brands b ON b.Id = p.BrandId
                JOIN Offers o ON o.ProductId = p.Id
                WHERE p.Id IN @Ids
                GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId",
                new { Ids = idList });

            var attributeRows = await db.QueryAsync<ProductAttributeRow>(@"
                SELECT ProductId, AttributeName, AttributeValue
                FROM ProductAttributes
                WHERE ProductId IN @Ids AND AttributeName <> 'Adet'
                ORDER BY AttributeName, AttributeValue",
                new { Ids = idList });

            var attributesByProduct = attributeRows
                .GroupBy(a => a.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(a => a.AttributeName)
                        .Select(ag => new { Name = ag.Key, Values = ag.Select(a => a.AttributeValue).Distinct().ToList() })
                        .ToList());

            IEnumerable<ProductSpecCompareRow> specRows = Enumerable.Empty<ProductSpecCompareRow>();
            try
            {
                specRows = await db.QueryAsync<ProductSpecCompareRow>(@"
                    SELECT ProductId, SpecGroup, SpecName, SpecValue
                    FROM ProductSpecs
                    WHERE ProductId IN @Ids",
                    new { Ids = idList });
            }
            catch
            {
            }

            var specsByProduct = specRows
                .GroupBy(s => s.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(s => s.SpecGroup)
                        .Select(sg => new { Group = sg.Key, Items = sg.Select(s => new { s.SpecName, s.SpecValue }).ToList() })
                        .ToList());

            var rank = idList.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);

            var items = products
                .OrderBy(p => rank.TryGetValue(p.Id, out var idx) ? idx : int.MaxValue)
                .Select(p =>
                {
                    var unitPrice = UnitPriceCalculator.Calculate(p.LowestPrice, p.SizeValue, p.SizeUnit, p.PackQuantity);
                    var discountPercent = p.OldPrice.HasValue && p.OldPrice > p.LowestPrice
                        ? Math.Round((p.OldPrice.Value - p.LowestPrice) / p.OldPrice.Value * 100, 0)
                        : (decimal?)null;
                    return new
                    {
                        p.Id,
                        p.CanonicalTitle,
                        p.Brand,
                        p.LowestPrice,
                        p.OfferCount,
                        p.ImageUrl,
                        UnitPrice = unitPrice?.Amount,
                        UnitLabel = unitPrice?.Label,
                        OldPrice = discountPercent.HasValue ? p.OldPrice : null,
                        DiscountPercent = discountPercent,
                        Attributes = attributesByProduct.TryGetValue(p.Id, out var attrs) ? attrs : [],
                        Specs = specsByProduct.TryGetValue(p.Id, out var specs) ? specs : []
                    };
                });

            return Results.Ok(items);
        });

        app.MapGet("/api/products", async (
            string? q,
            string[]? brand,
            string[]? store,
            string? category,
            string? sort,
            decimal? minPrice,
            decimal? maxPrice,
            string[]? attr,
            bool? onSale,
            int? page,
            int? pageSize,
            int? skip,
            LiveSearchService liveSearchService,
            ClaimsPrincipal principal,
            [FromServices] IConnectionMultiplexer? redis,
            [FromServices] IConfiguration configuration,
            [FromServices] IServiceScopeFactory scopeFactory) =>
        {
            int currentPage = Math.Max(1, page ?? 1);
            int limit = Math.Clamp(pageSize ?? 24, 1, 100);
            int offset = skip ?? (currentPage - 1) * limit;

            using var db = new SqlConnection(connectionString);

            var (whereClause, parameters, matchingCatIds) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
            ProductFilterHelpers.AppendBrandFilter(brand, ref whereClause, parameters);
            ProductFilterHelpers.AppendStoreFilter(store, ref whereClause, parameters);
            ProductFilterHelpers.AppendAttrGroupFilters(ProductFilterHelpers.ParseAttrGroups(attr), ref whereClause, parameters);

            var categoryTier = matchingCatIds is { Count: > 0 }
                ? "WHEN p.CategoryId IN @MatchingCatIds THEN 1"
                : "";
            var relevanceOrder = $@"
                CASE
                    WHEN p.Name COLLATE Turkish_100_CI_AI = @Query COLLATE Turkish_100_CI_AI THEN 0
                    {categoryTier}
                    WHEN p.Name COLLATE Turkish_100_CI_AI LIKE (@Query + ' %') COLLATE Turkish_100_CI_AI OR p.Name COLLATE Turkish_100_CI_AI LIKE ('% ' + @Query) COLLATE Turkish_100_CI_AI THEN 2
                    WHEN (' ' + p.Name + ' ') COLLATE Turkish_100_CI_AI LIKE ('% ' + @Query + ' %') COLLATE Turkish_100_CI_AI THEN 3
                    WHEN EXISTS (SELECT 1 FROM Brands rb WHERE rb.Id = p.BrandId AND (' ' + rb.Name + ' ') COLLATE Turkish_100_CI_AI LIKE ('% ' + @Query + ' %') COLLATE Turkish_100_CI_AI) THEN 4
                    ELSE 5
                END, LEN(p.Name) ASC, p.Name ASC";

            var hasQuery = !string.IsNullOrWhiteSpace(q);
            var orderBy = sort switch
            {
                "price_desc" => "(SELECT MIN(o6.Price) FROM Offers o6 WHERE o6.ProductId = p.Id) DESC",
                "price_asc" => "(SELECT MIN(o6.Price) FROM Offers o6 WHERE o6.ProductId = p.Id) ASC",
                "name_desc" => "p.Name DESC",
                "newest" => "p.CreatedAt DESC",
                _ when hasQuery => relevanceOrder,
                _ => "p.Name ASC"
            };

            var countSql = $"SELECT COUNT(*) FROM Products p {whereClause}";

            var idSql = $@"
                SELECT p.Id
                FROM Products p
                {whereClause}
                ORDER BY {orderBy}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            parameters.Add("Offset", offset);
            parameters.Add("PageSize", limit);

            var totalCount = await db.ExecuteScalarAsync<int>(countSql, parameters, commandTimeout: 30);
            var pageIds = (await db.QueryAsync<int>(idSql, parameters, commandTimeout: 30)).ToList();

            var isFirstBatch = offset == 0;

            if (isFirstBatch && !string.IsNullOrWhiteSpace(q) && totalCount > 0 && redis != null && redis.IsConnected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var rDb = redis.GetDatabase();
                        var today = DateTime.Now.ToString("yyyy-MM-dd");
                        var key = $"popular_searches:{today}";
                        var term = q.Trim().ToLowerInvariant();
                        await rDb.SortedSetIncrementAsync(key, term, 1);
                        await rDb.KeyExpireAsync(key, TimeSpan.FromDays(8));
                    }
                    catch
                    {
                    }
                });
            }

            int minResultsThreshold = configuration.GetValue<int?>("LiveSearch:MinResultsThreshold") ?? 20;
            bool noBrands = brand == null || brand.Length == 0 || brand.All(string.IsNullOrWhiteSpace);
            bool noStores = store == null || store.Length == 0 || store.All(string.IsNullOrWhiteSpace);
            bool needsLiveSearch = isFirstBatch && totalCount < minResultsThreshold && !string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(category) && noBrands && noStores;
            bool liveSearchPending = false;

            if (needsLiveSearch)
            {
                var term = q!.Trim();
                liveSearchPending = true;

                _ = Task.Run(async () =>
                {
                    using var bgScope = scopeFactory.CreateScope();
                    var bgLiveSearchService = bgScope.ServiceProvider.GetRequiredService<LiveSearchService>();
                    try
                    {
                        await bgLiveSearchService.ExecuteAsync(term);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[LiveSearch] Background search for '{term}' failed: {ex.Message}");
                    }
                });
            }

            var masterProducts = Enumerable.Empty<ProductListRow>();
            if (pageIds.Count > 0)
            {
                var pageRows = await db.QueryAsync<ProductListRow>(@"
                    SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                           MIN(o.Price) AS LowestPrice,
                           COUNT(o.Id) AS OfferCount,
                           p.ImageUrl,
                           p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                           MAX(o.RegularPrice) AS OldPrice,
                           (SELECT STRING_AGG(sName, ', ') FROM (SELECT DISTINCT s.Name AS sName FROM Offers o3 JOIN Sellers s ON s.Id = o3.SellerId WHERE o3.ProductId = p.Id AND o3.Price = (SELECT MIN(Price) FROM Offers WHERE ProductId = p.Id)) d) AS LowestPriceStore
                    FROM Products p
                    JOIN Brands b ON b.Id = p.BrandId
                    JOIN Offers o ON o.ProductId = p.Id
                    WHERE p.Id IN @PageIds
                    GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId",
                    new { PageIds = pageIds }, commandTimeout: 30);

                var rank = pageIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
                masterProducts = pageRows.OrderBy(mp => rank[mp.Id]);
            }

            if (isFirstBatch && !string.IsNullOrWhiteSpace(q) && totalCount > 0)
            {
                var searchUserId = AuthHelpers.GetUserId(principal);
                if (searchUserId is not null)
                {
                    var topMatch = masterProducts.FirstOrDefault();
                    var searchTerm = q.Trim();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var historyDb = new SqlConnection(connectionString);
                            await historyDb.ExecuteAsync(
                                "INSERT INTO UserSearchHistory (UserId, SearchTerm, ProductId, CategoryId) VALUES (@UserId, @SearchTerm, @ProductId, @CategoryId)",
                                new { UserId = searchUserId.Value, SearchTerm = searchTerm, ProductId = topMatch?.Id, CategoryId = topMatch?.CategoryId });
                        }
                        catch
                        {
                        }
                    });
                }
            }

            var items = masterProducts.Select(mp =>
            {
                var unitPrice = UnitPriceCalculator.Calculate(mp.LowestPrice, mp.SizeValue, mp.SizeUnit, mp.PackQuantity);
                var discountPercent = mp.OldPrice.HasValue && mp.OldPrice > mp.LowestPrice
                    ? Math.Round((mp.OldPrice.Value - mp.LowestPrice) / mp.OldPrice.Value * 100, 0)
                    : (decimal?)null;
                return new
                {
                    mp.Id,
                    mp.CanonicalTitle,
                    mp.Brand,
                    mp.LowestPrice,
                    mp.OfferCount,
                    mp.ImageUrl,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label,
                    OldPrice = discountPercent.HasValue ? mp.OldPrice : null,
                    DiscountPercent = discountPercent,
                    LowestPriceStore = FormatStoreName(mp.LowestPriceStore)
                };
            });

            int totalPages = (int)Math.Ceiling((double)totalCount / limit);

            return Results.Ok(new
            {
                items,
                totalCount,
                page = currentPage,
                pageSize = limit,
                totalPages,
                liveSearchPending
            });
        });

        app.MapGet("/api/products/recommended", async (int? limit, bool? onSale, ClaimsPrincipal principal) =>
        {
            int take = Math.Clamp(limit ?? 10, 1, 30);
            using var db = new SqlConnection(connectionString);

            var onSaleClause = onSale == true
                ? "AND EXISTS (SELECT 1 FROM Offers oDiscount WHERE oDiscount.ProductId = p.Id AND oDiscount.RegularPrice IS NOT NULL AND oDiscount.RegularPrice > oDiscount.Price)"
                : "";

            var userId = AuthHelpers.GetUserId(principal);
            var personalized = new List<ProductListRow>();
            var excludeIds = new List<int>();

            if (userId is not null)
            {
                const string interactionsCte = @"
                    ;WITH Interactions AS (
                        SELECT ph.ProductId, p.CategoryId, p.BrandId, 5 AS Weight
                        FROM PurchaseHistory ph JOIN Products p ON p.Id = ph.ProductId
                        WHERE ph.UserId = @UserId
                        UNION ALL
                        SELECT f.ProductId, p.CategoryId, p.BrandId, 3 AS Weight
                        FROM Favorites f JOIN Products p ON p.Id = f.ProductId
                        WHERE f.UserId = @UserId
                        UNION ALL
                        SELECT v.ProductId, p.CategoryId, p.BrandId, 2 AS Weight
                        FROM UserProductViews v JOIN Products p ON p.Id = v.ProductId
                        WHERE v.UserId = @UserId
                        UNION ALL
                        SELECT h.ProductId, h.CategoryId, p.BrandId, 1 AS Weight
                        FROM UserSearchHistory h JOIN Products p ON p.Id = h.ProductId
                        WHERE h.UserId = @UserId AND h.ProductId IS NOT NULL
                    )";

                const int MinInterestScore = 3;
                const int MaxRankedGroups = 5;

                var explicitCategoryIds = (await db.QueryAsync<int>(@"
                    SELECT DISTINCT p.CategoryId FROM PurchaseHistory ph JOIN Products p ON p.Id = ph.ProductId WHERE ph.UserId = @UserId AND p.CategoryId IS NOT NULL
                    UNION
                    SELECT DISTINCT p.CategoryId FROM Favorites f JOIN Products p ON p.Id = f.ProductId WHERE f.UserId = @UserId AND p.CategoryId IS NOT NULL",
                    new { UserId = userId.Value })).ToList();

                var explicitBrandIds = (await db.QueryAsync<int>(@"
                    SELECT DISTINCT p.BrandId FROM PurchaseHistory ph JOIN Products p ON p.Id = ph.ProductId WHERE ph.UserId = @UserId AND p.BrandId IS NOT NULL
                    UNION
                    SELECT DISTINCT p.BrandId FROM Favorites f JOIN Products p ON p.Id = f.ProductId WHERE f.UserId = @UserId AND p.BrandId IS NOT NULL",
                    new { UserId = userId.Value })).ToList();

                var remainingCategorySlots = Math.Max(0, MaxRankedGroups - explicitCategoryIds.Count);
                var rankedCategoryIds = remainingCategorySlots > 0
                    ? (await db.QueryAsync<int>(
                        interactionsCte + $@"
                        SELECT TOP {remainingCategorySlots} CategoryId FROM Interactions
                        WHERE CategoryId IS NOT NULL
                        {(explicitCategoryIds.Count > 0 ? "AND CategoryId NOT IN @ExplicitCategoryIds" : "")}
                        GROUP BY CategoryId
                        HAVING SUM(Weight) >= {MinInterestScore}
                        ORDER BY SUM(Weight) DESC",
                        new { UserId = userId.Value, ExplicitCategoryIds = explicitCategoryIds }))
                        .ToList()
                    : [];

                var remainingBrandSlots = Math.Max(0, MaxRankedGroups - explicitBrandIds.Count);
                var rankedBrandIds = remainingBrandSlots > 0
                    ? (await db.QueryAsync<int>(
                        interactionsCte + $@"
                        SELECT TOP {remainingBrandSlots} BrandId FROM Interactions
                        WHERE BrandId IS NOT NULL
                        {(explicitBrandIds.Count > 0 ? "AND BrandId NOT IN @ExplicitBrandIds" : "")}
                        GROUP BY BrandId
                        HAVING SUM(Weight) >= {MinInterestScore}
                        ORDER BY SUM(Weight) DESC",
                        new { UserId = userId.Value, ExplicitBrandIds = explicitBrandIds }))
                        .ToList()
                    : [];

                var topCategoryIds = explicitCategoryIds.Concat(rankedCategoryIds).ToList();
                var topBrandIds = explicitBrandIds.Concat(rankedBrandIds).ToList();

                excludeIds = (await db.QueryAsync<int>(
                    interactionsCte + "SELECT DISTINCT ProductId FROM Interactions",
                    new { UserId = userId.Value })).ToList();
                if (topCategoryIds.Count > 0 || topBrandIds.Count > 0)
                {
                    const double PersonalizedShare = 0.7;
                    int personalizedTake = Math.Max(1, (int)Math.Round(take * PersonalizedShare));

                    var conditions = new List<string>();
                    var dp = new DynamicParameters();
                    dp.Add("Take", personalizedTake);
                    if (topCategoryIds.Count > 0)
                    {
                        conditions.Add("p.CategoryId IN @TopCategoryIds");
                        dp.Add("TopCategoryIds", topCategoryIds);
                    }
                    if (topBrandIds.Count > 0)
                    {
                        conditions.Add("p.BrandId IN @TopBrandIds");
                        dp.Add("TopBrandIds", topBrandIds);
                    }

                    var excludeClause = "";
                    if (excludeIds.Count > 0)
                    {
                        excludeClause = "AND p.Id NOT IN @ExcludeIds";
                        dp.Add("ExcludeIds", excludeIds);
                    }

                    var sql = $@"
                        ;WITH Candidates AS (
                            SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                                   MIN(o.Price) AS LowestPrice, COUNT(o.Id) AS OfferCount,
                                   p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                                   MAX(o.RegularPrice) AS OldPrice,
                                   (SELECT STRING_AGG(sName, ', ') FROM (SELECT DISTINCT s.Name AS sName FROM Offers o3 JOIN Sellers s ON s.Id = o3.SellerId WHERE o3.ProductId = p.Id AND o3.Price = (SELECT MIN(Price) FROM Offers WHERE ProductId = p.Id)) d) AS LowestPriceStore,
                                   ROW_NUMBER() OVER (PARTITION BY p.CategoryId ORDER BY COUNT(o.Id) DESC) AS RankInCategory
                            FROM Products p
                            JOIN Brands b ON b.Id = p.BrandId
                            JOIN Offers o ON o.ProductId = p.Id
                            WHERE ({string.Join(" OR ", conditions)}) {excludeClause} {onSaleClause}
                            GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId
                        )
                        SELECT TOP (@Take) Id, CanonicalTitle, Brand, LowestPrice, OfferCount, ImageUrl, SizeValue, SizeUnit, PackQuantity, CategoryId, OldPrice, LowestPriceStore
                        FROM Candidates
                        ORDER BY RankInCategory ASC, OfferCount DESC";

                    personalized = (await db.QueryAsync<ProductListRow>(sql, dp, commandTimeout: 30)).ToList();
                }
            }

            var rows = personalized;
            if (rows.Count < take)
            {
                var alreadyIncluded = excludeIds.Concat(rows.Select(r => r.Id)).Distinct().ToList();
                var fillParams = new DynamicParameters();
                fillParams.Add("FillCount", take - rows.Count);

                var fillConditions = new List<string>();
                if (alreadyIncluded.Count > 0)
                {
                    fillConditions.Add("p.Id NOT IN @AlreadyIncluded");
                    fillParams.Add("AlreadyIncluded", alreadyIncluded);
                }
                if (onSale == true)
                {
                    fillConditions.Add("EXISTS (SELECT 1 FROM Offers oDiscount WHERE oDiscount.ProductId = p.Id AND oDiscount.RegularPrice IS NOT NULL AND oDiscount.RegularPrice > oDiscount.Price)");
                }
                var fillWhereClause = fillConditions.Count > 0 ? "WHERE " + string.Join(" AND ", fillConditions) : "";

                var fillRows = (await db.QueryAsync<ProductListRow>($@"
                    SELECT TOP (@FillCount) p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                           MIN(o.Price) AS LowestPrice, COUNT(o.Id) AS OfferCount,
                           p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId,
                           MAX(o.RegularPrice) AS OldPrice,
                           (SELECT STRING_AGG(sName, ', ') FROM (SELECT DISTINCT s.Name AS sName FROM Offers o3 JOIN Sellers s ON s.Id = o3.SellerId WHERE o3.ProductId = p.Id AND o3.Price = (SELECT MIN(Price) FROM Offers WHERE ProductId = p.Id)) d) AS LowestPriceStore
                    FROM Products p
                    JOIN Brands b ON b.Id = p.BrandId
                    JOIN Offers o ON o.ProductId = p.Id
                    {fillWhereClause}
                    GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId
                    ORDER BY COUNT(o.Id) DESC, p.Id DESC",
                    fillParams, commandTimeout: 30)).ToList();

                rows = rows.Concat(fillRows).ToList();
            }

            var items = rows.Select(mp =>
            {
                var unitPrice = UnitPriceCalculator.Calculate(mp.LowestPrice, mp.SizeValue, mp.SizeUnit, mp.PackQuantity);
                var discountPercent = mp.OldPrice.HasValue && mp.OldPrice > mp.LowestPrice
                    ? Math.Round((mp.OldPrice.Value - mp.LowestPrice) / mp.OldPrice.Value * 100, 0)
                    : (decimal?)null;
                return new
                {
                    mp.Id,
                    mp.CanonicalTitle,
                    mp.Brand,
                    mp.LowestPrice,
                    mp.OfferCount,
                    mp.ImageUrl,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label,
                    OldPrice = discountPercent.HasValue ? mp.OldPrice : null,
                    DiscountPercent = discountPercent,
                    LowestPriceStore = FormatStoreName(mp.LowestPriceStore)
                };
            });

            return Results.Ok(new { items });
        });

        app.MapGet("/api/products/{productId}/price-history", async (int productId) =>
        {
            using var db = new SqlConnection(connectionString);
            var history = await db.QueryAsync(@"
                SELECT oph.Price, oph.RecordedAt, s.Name AS SellerName
                FROM OffersPriceHistory oph
                JOIN Offers o ON o.Id = oph.OfferId
                JOIN Sellers s ON s.Id = o.SellerId
                WHERE o.ProductId = @ProductId
                ORDER BY oph.RecordedAt ASC",
                new { ProductId = productId });

            var grouped = history
                .GroupBy(h => Convert.ToDateTime(h.RecordedAt).ToString("yyyy-MM-dd"))
                .Select(g => new
                {
                    Date = g.Key,
                    MinPrice = g.Min(x => (decimal)x.Price),
                    AvgPrice = Math.Round(g.Average(x => (decimal)x.Price), 2),
                    Offers = g.Select(x => new { SellerName = FormatStoreName((string)x.SellerName), Price = (decimal)x.Price })
                })
                .ToList();

            return Results.Ok(grouped);
        });
    }

    private static string? FormatStoreName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        var parts = rawName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(", ", parts.Select(FormatSingleStoreName));
    }

    private static string FormatSingleStoreName(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "bim" => "BİM",
            "a101" => "A101",
            "sok" => "Şok",
            "migros" => "Migros",
            "carrefour" => "CarrefourSA",
            "hakmarexpress" => "Hakmar Express",
            "macrocenter" => "Macrocenter",
            "evidea" => "Evidea",
            "ikea" => "IKEA",
            "mion" => "Mion",
            "marketfiyati" => "Market Fiyatı",
            var other => other.Length > 0 ? char.ToUpperInvariant(other[0]) + other.Substring(1) : other
        };
    }
}

public record PriceHistoryDbRow(decimal Price, DateTime RecordedAt, string SellerName);
public record CurrentOfferDbRow(decimal Price, string SellerName);
public record OfferDetailDto(string SellerName, decimal Price);
public class PriceHistoryGroupDto
{
    public string Date { get; set; } = string.Empty;
    public decimal MinPrice { get; set; }
    public decimal AvgPrice { get; set; }
    public List<OfferDetailDto> Offers { get; set; } = new();
}

public record OfferRow(int SellerId, string CanonicalTitle, string Source, decimal Price, decimal? RegularPrice, string? ImageUrl,
    string ProductUrl, string? StoreName, string? ExternalTitle, decimal? SizeValue, string? SizeUnit, int PackQuantity);

public record ProductDetailRow(int Id, string CanonicalTitle, string? Brand, string? ImageUrl,
    decimal? SizeValue, string? SizeUnit, int PackQuantity, string? Sku, int? CategoryId);

public record ProductListRow(int Id, string CanonicalTitle, string? Brand, decimal LowestPrice,
    int OfferCount, string? ImageUrl, decimal? SizeValue, string? SizeUnit, int PackQuantity, int? CategoryId,
    decimal? OldPrice, string? LowestPriceStore);

public record TargetProductRow(string Name, int? CategoryId, int? BrandId);

public record SimilarProductRow(int Id, string CanonicalTitle, string? Brand, decimal LowestPrice, int OfferCount, string? ImageUrl, bool SameCategory, decimal? UnitPrice, string? UnitLabel, string? LowestPriceStore);

public record ScoredProduct(ProductListRow Product, double Score, bool SameCategory);

public record ProductAttributeRow(int ProductId, string AttributeName, string AttributeValue);

public record ProductSpecRow(string SpecGroup, string SpecName, string SpecValue);

public record ProductSpecCompareRow(int ProductId, string SpecGroup, string SpecName, string SpecValue);
