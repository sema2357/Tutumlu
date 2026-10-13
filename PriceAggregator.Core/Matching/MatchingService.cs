using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core.Categorization;
using PriceAggregator.Core.Helpers;
using PriceAggregator.Core.Pricing;

namespace PriceAggregator.Core.Matching;

public class MatchingService
{
    public const double MatchThreshold = 0.80;

    private static readonly ConcurrentDictionary<string, int> SellerCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, int> BrandCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _connectionString;

    public MatchingService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static async Task<(int? MatchId, double Score)> FindBestMatchAsync(
        SqlConnection db, int brandId, decimal? size, string? unit, int packQty, string title, string safeBrand,
        int? excludeProductId = null)
    {
        await TitleSimilarity.EnsureModifiersLoadedAsync(db);
        var variantKey = VariantParser.ExtractVariant(title);

        var candidates = await db.QueryAsync<(int Id, string CanonicalTitle)>(@"
            SELECT Id, Name AS CanonicalTitle FROM Products
            WHERE BrandId = @BrandId
            AND (SizeValue = @Size OR (SizeValue IS NULL AND @Size IS NULL))
            AND (SizeUnit = @Unit OR (SizeUnit IS NULL AND @Unit IS NULL))
            AND PackQuantity = @PackQty
            AND (
                (@VariantKey IS NULL AND VariantKey IS NULL)
                OR (@VariantKey IS NOT NULL AND VariantKey = @VariantKey)
            )
            AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
            new { BrandId = brandId, Size = size, Unit = unit, PackQty = packQty, VariantKey = variantKey, ExcludeId = excludeProductId });

        int? bestMatchId = null;
        double bestScore = 0.0;

        foreach (var candidate in candidates)
        {
            double score = TitleSimilarity.Calculate(candidate.CanonicalTitle, title, safeBrand, size, packQty);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatchId = candidate.Id;
            }
        }

        return (bestMatchId, bestScore);
    }

    public async Task<bool> MatchProductAsync(int sellerProductId, ProductDto product, string source, IProductSpecScraper? specScraper = null, int? forcedCategoryId = null)
    {
        var results = await MatchProductsBatchAsync([(sellerProductId, product)], source, specScraper, forcedCategoryId);
        return results.Count > 0 && results[0];
    }

    private record PreparedMatch(
        int SellerProductId, ProductDto Product, decimal? Size, string? Unit, int PackQty,
        int SellerId, int BrandId, int? BestMatchId, double BestScore, bool NeedsCategory);

    public async Task<List<bool>> MatchProductsBatchAsync(
        List<(int SellerProductId, ProductDto Product)> items, string source, IProductSpecScraper? specScraper = null, int? forcedCategoryId = null)
    {
        if (items.Count == 0) return new List<bool>();

        // Aynı batch içinde aynı mağaza ürünü mükerrer gelirse tekilleştir
        items = items.DistinctBy(x => x.SellerProductId).ToList();

        var results = new bool[items.Count];
        var prepared = new PreparedMatch?[items.Count];

        using (var db = new SqlConnection(_connectionString))
        {
            await db.OpenAsync();
            for (var i = 0; i < items.Count; i++)
            {
                var (sellerProductId, product) = items[i];
                try
                {
                    var safeBrand = string.IsNullOrWhiteSpace(product.Brand) ? "Unknown" : product.Brand;
                    var (size, unit, packQty) = SizeParser.Parse(product);

                    var sellerId = await FindOrCreateSellerAsync(db, source);
                    var brandId = await FindOrCreateBrandAsync(db, safeBrand);

                    // 1. Bu satıcı ürünü (SellerProductId) veritabanında zaten bir ürüne bağlı mı?
                    var existingProductId = await db.QuerySingleOrDefaultAsync<int?>(
                        "SELECT TOP 1 ProductId FROM Offers WHERE SellerProductId = @SellerProductId",
                        new { SellerProductId = sellerProductId });

                    int? bestMatchId;
                    double bestScore;
                    bool needsCategory;

                    if (existingProductId.HasValue)
                    {
                        // Zaten bu mağaza ürünü bir kanonik ürüne bağlı! Mükerrer ürün açma!
                        bestMatchId = existingProductId.Value;
                        bestScore = 1.0;
                        needsCategory = false;
                    }
                    else
                    {
                        var match = await FindBestMatchAsync(db, brandId, size, unit, packQty, product.Title, safeBrand);
                        bestMatchId = match.MatchId;
                        bestScore = match.Score;

                        if (bestMatchId.HasValue && bestScore >= MatchThreshold)
                        {
                            needsCategory = !await db.QuerySingleAsync<bool>(
                                @"SELECT CAST(CASE WHEN c.Id IS NOT NULL AND c.ParentCategoryId IS NOT NULL THEN 1 ELSE 0 END AS BIT)
                                  FROM Products p LEFT JOIN Categories c ON c.Id = p.CategoryId
                                  WHERE p.Id = @Id",
                                new { Id = bestMatchId.Value });
                        }
                        else
                        {
                            needsCategory = true;
                        }
                    }

                    prepared[i] = new PreparedMatch(sellerProductId, product, size, unit, packQty, sellerId, brandId, bestMatchId, bestScore, needsCategory);
                }
                catch (SqlException ex)
                {
                    Logger.Log($"[Matching] DB error preparing match for {sellerProductId} ('{product.Title}'): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Matching] Unexpected error preparing match for {sellerProductId} ('{product.Title}'): {ex.Message}");
                }
            }
        }

        var needsCategoryIndices = Enumerable.Range(0, prepared.Length)
            .Where(i => prepared[i]?.NeedsCategory == true)
            .ToList();

        var resolvedCategoryIds = new int?[items.Count];
        if (needsCategoryIndices.Count > 0)
        {
            using var db = new SqlConnection(_connectionString);
            var stillNeedsCategoryIndices = new List<int>();
            var resolveItems = new List<(ProductDto Product, string Source)>();

            for (var k = 0; k < needsCategoryIndices.Count; k++)
            {
                var idx = needsCategoryIndices[k];
                var product = prepared[idx]!.Product;

                int? mappedId = null;
                var rawPath = (product.CategoryPath != null && product.CategoryPath.Count > 0)
                    ? string.Join(" > ", product.CategoryPath)
                    : null;

                if (!string.IsNullOrWhiteSpace(product.StoreCategoryId) || !string.IsNullOrWhiteSpace(rawPath))
                {
                    // 1. Check CategoryMappings via ShopCategories
                    mappedId = await db.QuerySingleOrDefaultAsync<int?>(@"
                        SELECT TOP 1 cm.CanonicalCategoryId 
                        FROM dbo.CategoryMappings cm
                        JOIN dbo.ShopCategories sc ON cm.ShopCategoryId = sc.Id
                        JOIN dbo.Sellers s ON sc.ShopId = s.Id
                        WHERE s.Name = @Source 
                          AND (
                              (@StoreCategoryId IS NOT NULL AND (sc.VendorExternalId = @StoreCategoryId OR sc.RawPath = @StoreCategoryId))
                              OR (@RawPath IS NOT NULL AND sc.RawPath = @RawPath)
                          )
                          AND cm.CanonicalCategoryId IS NOT NULL", 
                        new { Source = source, StoreCategoryId = product.StoreCategoryId, RawPath = rawPath });

                    // 2. Check SellerCategories
                    if (!mappedId.HasValue)
                    {
                        mappedId = await db.QuerySingleOrDefaultAsync<int?>(@"
                            SELECT TOP 1 sc.TargetCategoryId 
                            FROM dbo.SellerCategories sc
                            JOIN dbo.Sellers s ON sc.SellerId = s.Id
                            WHERE s.Name = @Source 
                              AND (
                                  (@StoreCategoryId IS NOT NULL AND (sc.ExternalCode = @StoreCategoryId OR sc.CategoryPath = @StoreCategoryId))
                                  OR (@RawPath IS NOT NULL AND sc.CategoryPath = @RawPath)
                              )
                              AND sc.TargetCategoryId IS NOT NULL",
                            new { Source = source, StoreCategoryId = product.StoreCategoryId, RawPath = rawPath });
                    }
                }

                if (forcedCategoryId.HasValue)
                {
                    resolvedCategoryIds[idx] = forcedCategoryId.Value;
                }
                else if (mappedId.HasValue)
                {
                    resolvedCategoryIds[idx] = mappedId.Value;
                }
                else
                {
                    stillNeedsCategoryIndices.Add(idx);
                    resolveItems.Add((product, source));
                }
            }

            if (stillNeedsCategoryIndices.Count > 0)
            {
                var resolved = await CategoryResolver.ResolveBatchAsync(db, resolveItems);
                for (var k = 0; k < stillNeedsCategoryIndices.Count; k++)
                {
                    var targetIdx = stillNeedsCategoryIndices[k];
                    resolvedCategoryIds[targetIdx] = resolved[k];
                }
            }
        }

        using (var db = new SqlConnection(_connectionString))
        {
            await db.OpenAsync();
            for (var i = 0; i < items.Count; i++)
            {
                var plan = prepared[i];
                if (plan is null) { results[i] = false; continue; }

                try
                {
                    int masterId;
                    string matchStatus, matchedBy;
                    string? matchNotes;

                    if (plan.BestMatchId.HasValue && plan.BestScore >= MatchThreshold)
                    {
                        masterId = plan.BestMatchId.Value;
                        matchStatus = "Matched";
                        matchedBy = plan.BestScore >= 1.0 ? "ExistingSellerProduct" : "TitleSimilarity";
                        matchNotes = $"score={plan.BestScore:F2}";

                        if (plan.NeedsCategory && resolvedCategoryIds[i] is not null)
                        {
                            await db.ExecuteAsync(
                                @"UPDATE p SET CategoryId = @CategoryId
                                  FROM Products p LEFT JOIN Categories c ON c.Id = p.CategoryId
                                  WHERE p.Id = @Id AND (c.Id IS NULL OR c.ParentCategoryId IS NULL)",
                                new { CategoryId = resolvedCategoryIds[i], Id = masterId });
                        }
                    }
                    else
                    {
                        matchStatus = "Created";
                        matchedBy = "NewProduct";
                        matchNotes = plan.BestMatchId.HasValue ? $"bestScore={plan.BestScore:F2} (below threshold)" : null;

                        var variantKey = VariantParser.ExtractVariant(plan.Product.Title);
                        masterId = await db.QuerySingleAsync<int>(@"
                        INSERT INTO Products (Name, BrandId, SizeValue, SizeUnit, PackQuantity, CategoryId, ImageUrl, VariantKey)
                        OUTPUT INSERTED.Id
                        VALUES (@Title, @BrandId, @Size, @Unit, @PackQty, @CategoryId, @ImageUrl, @VariantKey)",
                        new { Title = plan.Product.Title, BrandId = plan.BrandId, Size = plan.Size, Unit = plan.Unit, PackQty = plan.PackQty, CategoryId = resolvedCategoryIds[i], plan.Product.ImageUrl, VariantKey = variantKey });
                    }

                    await db.ExecuteAsync(
                        "INSERT INTO ProductMatchingLogs (SellerProductId, ProductId, Status, MatchedBy, Notes) VALUES (@SellerProductId, @ProductId, @Status, @MatchedBy, @Notes)",
                        new { SellerProductId = plan.SellerProductId, ProductId = masterId, Status = matchStatus, MatchedBy = matchedBy, Notes = matchNotes });

                    var regularPrice = plan.Product.RegularPrice > plan.Product.Price ? plan.Product.RegularPrice : (decimal?)null;
                    await UpsertOfferAsync(db, masterId, plan.SellerId, plan.SellerProductId, plan.Product.Price, regularPrice);
                    await UpsertAttributesAsync(db, masterId, plan.Product, plan.Size, plan.Unit, plan.PackQty, source);

                    if (specScraper is not null)
                        await UpsertSpecsAsync(db, masterId, plan.Product, specScraper);

                    results[i] = true;
                }
                catch (SqlException ex)
                {
                    Logger.Log($"[Matching] DB error finalizing match for {plan.SellerProductId} ('{plan.Product.Title}'): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Matching] Unexpected error finalizing match for {plan.SellerProductId} ('{plan.Product.Title}'): {ex.Message}");
                }
            }
        }

        return results.ToList();
    }

    private static async Task<int> FindOrCreateBrandAsync(SqlConnection db, string name)
    {
        var canonicalName = TurkishTextHelper.ToTitleCase(name);
        if (string.IsNullOrWhiteSpace(canonicalName)) canonicalName = "Diğer";

        if (BrandCache.TryGetValue(canonicalName, out var cachedId))
            return cachedId;

        var existing = await db.QuerySingleOrDefaultAsync<int?>(
            "SELECT TOP 1 Id FROM Brands WHERE Name = @Name OR Name = @RawName", 
            new { Name = canonicalName, RawName = name.Trim() });
        if (existing.HasValue)
        {
            BrandCache[canonicalName] = existing.Value;
            return existing.Value;
        }

        try
        {
            var newId = await db.QuerySingleAsync<int>(
                "INSERT INTO Brands (Name) OUTPUT INSERTED.Id VALUES (@Name)", new { Name = canonicalName });
            BrandCache[canonicalName] = newId;
            return newId;
        }
        catch (SqlException)
        {
            var foundId = await db.QuerySingleAsync<int>(
                "SELECT TOP 1 Id FROM Brands WHERE Name = @Name OR Name = @RawName", 
                new { Name = canonicalName, RawName = name.Trim() });
            BrandCache[canonicalName] = foundId;
            return foundId;
        }
    }

    private static async Task<int> FindOrCreateSellerAsync(SqlConnection db, string name)
    {
        if (SellerCache.TryGetValue(name, out var cachedId))
            return cachedId;

        var existing = await db.QuerySingleOrDefaultAsync<int?>(
            "SELECT Id FROM Sellers WHERE Name = @Name", new { Name = name });
        if (existing.HasValue)
        {
            SellerCache[name] = existing.Value;
            return existing.Value;
        }

        try
        {
            var newId = await db.QuerySingleAsync<int>(
                "INSERT INTO Sellers (Name) OUTPUT INSERTED.Id VALUES (@Name)", new { Name = name });
            SellerCache[name] = newId;
            return newId;
        }
        catch (SqlException)
        {
            var foundId = await db.QuerySingleAsync<int>(
                "SELECT Id FROM Sellers WHERE Name = @Name", new { Name = name });
            SellerCache[name] = foundId;
            return foundId;
        }
    }

    private static async Task UpsertOfferAsync(SqlConnection db, int productId, int sellerId, int sellerProductId, decimal price, decimal? regularPrice)
    {
        var existingOffer = await db.QuerySingleOrDefaultAsync<OfferRow>(
            "SELECT TOP 1 Id, Price FROM Offers WHERE SellerProductId = @SellerProductId",
            new { SellerProductId = sellerProductId });

        if (existingOffer is null)
        {
            existingOffer = await db.QuerySingleOrDefaultAsync<OfferRow>(
                "SELECT TOP 1 Id, Price FROM Offers WHERE ProductId = @ProductId AND SellerId = @SellerId",
                new { ProductId = productId, SellerId = sellerId });
        }

        int offerId;
        if (existingOffer is not null)
        {
            offerId = existingOffer.Id;

            if (existingOffer.Price != price)
            {
                await db.ExecuteAsync(
                    "INSERT INTO OffersPriceHistory (OfferId, Price) VALUES (@OfferId, @Price)",
                    new { OfferId = offerId, Price = price });
            }

            await db.ExecuteAsync(
                "UPDATE Offers SET ProductId = @ProductId, Price = @Price, RegularPrice = @RegularPrice, SellerProductId = @SellerProductId, LastUpdatedAt = GETDATE() WHERE Id = @Id",
                new { ProductId = productId, Price = price, RegularPrice = regularPrice, SellerProductId = sellerProductId, Id = offerId });
        }
        else
        {
            offerId = await db.QuerySingleAsync<int>(@"
                INSERT INTO Offers (ProductId, SellerId, SellerProductId, Price, RegularPrice)
                OUTPUT INSERTED.Id
                VALUES (@ProductId, @SellerId, @SellerProductId, @Price, @RegularPrice)",
                new { ProductId = productId, SellerId = sellerId, SellerProductId = sellerProductId, Price = price, RegularPrice = regularPrice });

            await db.ExecuteAsync(
                "INSERT INTO OffersPriceHistory (OfferId, Price) VALUES (@OfferId, @Price)",
                new { OfferId = offerId, Price = price });
        }
    }

    private static async Task UpsertAttributesAsync(
        SqlConnection db, int productId, ProductDto product, decimal? size, string? unit, int packQty, string source)
    {
        var attributes = new List<(string Name, string Value)>();

        if (product.Attributes is not null)
        {
            foreach (var (name, rawValue) in product.Attributes)
            {
                foreach (var part in SplitCompoundValue(rawValue))
                    attributes.Add((name, part));
            }
        }

        foreach (var sizeAttr in SizeFormatter.FormatAttributes(size, unit, packQty))
            attributes.Add(sizeAttr);

        // Başlıktan tespit edilen aroma, edisyon ve diyet niteleyicilerini otomatik olarak ProductAttributes'a ekle:
        var flavorAttrs = PriceAggregator.Core.Categorization.FlavorExtractor.Extract(product.Title);
        foreach (var (fName, fVal) in flavorAttrs)
            attributes.Add((fName, fVal));

        // Teknoloji ürünleri için RAM, Dahili Hafıza, Renk, Boyut, Bağlantı niteleyicilerini otomatik ekle:
        var techAttrs = PriceAggregator.Core.Categorization.TechAttributeExtractor.Extract(product.Title);
        foreach (var (tName, tVal) in techAttrs)
            attributes.Add((tName, tVal));

        foreach (var (name, value) in attributes)
        {
            try
            {
                await db.ExecuteAsync(
                    @"IF NOT EXISTS (SELECT 1 FROM ProductAttributes WHERE ProductId = @ProductId AND AttributeName = @Name AND AttributeValue = @Value)
                      INSERT INTO ProductAttributes (ProductId, AttributeName, AttributeValue) VALUES (@ProductId, @Name, @Value)",
                    new { ProductId = productId, Name = name, Value = value });
            }
            catch (SqlException)
            {
            }
        }
    }

    private static async Task UpsertSpecsAsync(SqlConnection db, int productId, ProductDto product, IProductSpecScraper specScraper)
    {
        var alreadyFetched = await db.ExecuteScalarAsync<bool>(
            "SELECT CAST(CASE WHEN SpecsFetchedAt IS NULL THEN 0 ELSE 1 END AS BIT) FROM Products WHERE Id = @Id",
            new { Id = productId });
        if (alreadyFetched) return;

        List<ProductSpec> specs;
        try
        {
            specs = await specScraper.FetchSpecsAsync(product);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Specs] Failed to fetch specs for product {productId}: {ex.Message}");
            return;
        }

        foreach (var spec in specs)
        {
            try
            {
                await db.ExecuteAsync(
                    @"IF NOT EXISTS (SELECT 1 FROM ProductSpecs WHERE ProductId = @ProductId AND SpecGroup = @Group AND SpecName = @Name)
                      INSERT INTO ProductSpecs (ProductId, SpecGroup, SpecName, SpecValue) VALUES (@ProductId, @Group, @Name, @Value)",
                    new { ProductId = productId, Group = spec.Group, Name = spec.Name, Value = spec.Value });
            }
            catch (SqlException)
            {
            }
        }

        await db.ExecuteAsync("UPDATE Products SET SpecsFetchedAt = GETDATE() WHERE Id = @Id", new { Id = productId });
    }

    private static IEnumerable<string> SplitCompoundValue(string rawValue)
    {
        return rawValue
            .Split(['/', '-', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(TurkishTextHelper.ToTitleCase)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.Ordinal);
    }

    private record OfferRow(int Id, decimal Price);
}
