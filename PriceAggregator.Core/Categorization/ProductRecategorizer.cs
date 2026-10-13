using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core.Data;
using PriceAggregator.Core.Helpers;

namespace PriceAggregator.Core.Categorization;

public static class ProductRecategorizer
{
    public record RecategorizeResult(int Total, int Categorized, int DeletedUnmatched);

    public static async Task<RecategorizeResult> RunAsync(string connectionString)
    {
        using var db = new SqlConnection(connectionString);
        await db.OpenAsync();

        Logger.Log("[Recategorizer] Kategori taksonomisi ve kurallar yukleniyor...");
        var taxonomy = await CategoryResolver.LoadTaxonomyAsync(db);
        var catalog = await CategoryKeywordCatalog.LoadAsync(db);

        // Load SellerCategories mapped codes
        var sellerCatMappings = (await db.QueryAsync<(string ExternalCode, int TargetCategoryId)>(
            "SELECT ExternalCode, TargetCategoryId FROM SellerCategories WHERE TargetCategoryId IS NOT NULL")).ToList();
        var sellerCatDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in sellerCatMappings)
        {
            if (!string.IsNullOrWhiteSpace(m.ExternalCode))
                sellerCatDict[m.ExternalCode.Trim()] = m.TargetCategoryId;
        }

        // Fetch all products with their associated offers and seller products
        Logger.Log("[Recategorizer] Tum urunler ve magaza teklifleri veritabanindan cekiliyor...");
        var rawRows = (await db.QueryAsync<ProductOfferMeta>(@"
            SELECT p.Id AS ProductId, p.Name AS Title, b.Name AS Brand,
                   sp.OriginalCategory, sp.ExternalTitle, sp.SellerProductCode,
                   s.Name AS SellerName
            FROM Products p
            LEFT JOIN Brands b ON b.Id = p.BrandId
            LEFT JOIN Offers o ON o.ProductId = p.Id
            LEFT JOIN SellerProducts sp ON sp.Id = o.SellerProductId
            LEFT JOIN Sellers s ON s.Id = o.SellerId")).ToList();

        var productsGrouped = rawRows.GroupBy(r => r.ProductId).ToList();
        Logger.Log($"[Recategorizer] Toplam {productsGrouped.Count} adet tekil urun analize aliniyor...");

        var toUpdate = new List<(int ProductId, int CategoryId)>();
        var toDeleteIds = new List<int>();

        int processed = 0;
        foreach (var group in productsGrouped)
        {
            int productId = group.Key;
            var first = group.First();
            string title = first.Title;
            string? brand = first.Brand;

            // Collect all non-empty original category strings
            var origCategories = group
                .Select(g => g.OriginalCategory)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int? resolvedId = null;

            // 1. Check SellerCategories dictionary match
            foreach (var origCat in origCategories)
            {
                if (sellerCatDict.TryGetValue(origCat, out int mappedId))
                {
                    resolvedId = mappedId;
                    break;
                }
            }

            // 2. Check CategoryResolver with breadcrumb paths
            if (!resolvedId.HasValue && origCategories.Count > 0)
            {
                foreach (var origCat in origCategories)
                {
                    var segments = origCat.Split(new[] { " > ", " » ", ">", "/" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(s => !CategoryResolver.IgnoredBreadcrumbRoots.Contains(s))
                        .ToList();

                    if (segments.Count > 0)
                    {
                        var productDto = new ProductDto
                        {
                            Title = title,
                            Brand = brand,
                            CategoryPath = segments,
                            CategoryName = segments[^1],
                            ParentCategoryName = segments.Count > 1 ? segments[^2] : null,
                            RootCategoryName = segments[0]
                        };

                        var sellerName = group.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.SellerName))?.SellerName ?? "migros";
                        resolvedId = await CategoryResolver.ResolveAsync(db, productDto, sellerName);
                        if (resolvedId.HasValue) break;
                    }
                }
            }

            // 3. Check CategoryKeywordCatalog rules
            if (!resolvedId.HasValue)
            {
                var productDto = new ProductDto
                {
                    Title = title,
                    Brand = brand,
                    CategoryName = origCategories.FirstOrDefault()
                };
                resolvedId = CategoryKeywordCatalog.MatchProduct(productDto, catalog);
            }

            // 4. Fallback by Title tokens against taxonomy
            if (!resolvedId.HasValue)
            {
                resolvedId = CategoryResolver.ResolveByTitleFallback(taxonomy, title);
            }

            if (resolvedId.HasValue)
            {
                toUpdate.Add((productId, resolvedId.Value));
            }
            else
            {
                toDeleteIds.Add(productId);
            }

            processed++;
            if (processed % 1000 == 0)
            {
                Logger.Log($"[Recategorizer] Ilerleme: {processed}/{productsGrouped.Count} urun analiz edildi (Kategorilenen: {toUpdate.Count}, Uymayan/Silinecek: {toDeleteIds.Count})...");
            }
        }

        // Apply Updates in batches of 500
        Logger.Log($"[Recategorizer] {toUpdate.Count} urunun kategorisi guncelleniyor...");
        const int batchSize = 500;
        for (int i = 0; i < toUpdate.Count; i += batchSize)
        {
            var batch = toUpdate.Skip(i).Take(batchSize).ToList();
            using var tx = db.BeginTransaction();
            foreach (var item in batch)
            {
                await db.ExecuteAsync(
                    "UPDATE Products SET CategoryId = @CategoryId WHERE Id = @ProductId",
                    new { item.CategoryId, item.ProductId }, tx);
            }
            tx.Commit();
        }

        // Delete unmatched products
        if (toDeleteIds.Count > 0)
        {
            Logger.Log($"[Recategorizer] Hicbir kategoriye uymayan {toDeleteIds.Count} urun veritabanindan siliniyor...");
            for (int i = 0; i < toDeleteIds.Count; i += batchSize)
            {
                var batch = toDeleteIds.Skip(i).Take(batchSize).ToList();
                using var tx = db.BeginTransaction();
                await db.ExecuteAsync("DELETE FROM UserProductViews WHERE ProductId IN @Ids", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM PurchaseHistory WHERE ProductId IN @Ids", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM Favorites WHERE ProductId IN @Ids", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM ProductMatchingLogs WHERE ProductId IN @Ids", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM ProductSpecs WHERE ProductId IN @Ids", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM ProductAttributes WHERE ProductId IN @Ids", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM OffersPriceHistory WHERE OfferId IN (SELECT Id FROM Offers WHERE ProductId IN @Ids)", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM Offers WHERE ProductId IN @Ids", new { Ids = batch }, tx);
                await db.ExecuteAsync("DELETE FROM Products WHERE Id IN @Ids", new { Ids = batch }, tx);
                tx.Commit();
            }
            Logger.Log($"[Recategorizer] {toDeleteIds.Count} urun ve bagli kayitlari basariyla temizlendi.");
        }

        Logger.Log($"[Recategorizer] Islem tamamlandi! Toplam: {productsGrouped.Count}, Kategorilenen: {toUpdate.Count}, Silinen: {toDeleteIds.Count}");
        return new RecategorizeResult(productsGrouped.Count, toUpdate.Count, toDeleteIds.Count);
    }

    private class ProductOfferMeta
    {
        public int ProductId { get; set; }
        public string Title { get; set; } = "";
        public string? Brand { get; set; }
        public string? OriginalCategory { get; set; }
        public string? ExternalTitle { get; set; }
        public string? SellerProductCode { get; set; }
        public string? SellerName { get; set; }
    }
}
