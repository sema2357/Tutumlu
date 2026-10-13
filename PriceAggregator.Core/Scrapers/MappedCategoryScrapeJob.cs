using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core.Categorization;
using PriceAggregator.Core.Data;
using PriceAggregator.Core.Matching;

namespace PriceAggregator.Core.Scrapers;

public class StoreCategoryMappingDto
{
    public string StoreName { get; set; } = "";
    public string StoreCategoryId { get; set; } = "";
    public int TargetCategoryId { get; set; }
    public string? CategoryPath { get; set; }
    public string? CanonicalCategoryName { get; set; }
}

public class MappedCategoryScrapeJob
{
    private readonly string _connectionString;
    private readonly ProductRepository _repo;
    private readonly MatchingService _matchingService;
    private readonly List<ICategoryScraper> _categoryScrapers;

    public MappedCategoryScrapeJob(string connectionString, MatchingService matchingService, IEnumerable<IProductScraper> scrapers)
    {
        _connectionString = connectionString;
        _repo = new ProductRepository(connectionString);
        _matchingService = matchingService;
        _categoryScrapers = scrapers.OfType<ICategoryScraper>().ToList();
    }

    public async Task RunAsync(
        int maxProductsPerCategory = 200,
        string? storeFilter = null,
        string? categoryFilter = null)
    {
        Logger.Log($"=========================================================");
        Logger.Log($"   DOĞRUDAN KATEGORİ (TREE-BASED) ÜRÜN ÇEKME BAŞLATILIYOR ");
        Logger.Log($"   Hedef: Her kategoriden en fazla {maxProductsPerCategory} ürün");
        if (!string.IsNullOrWhiteSpace(storeFilter))
            Logger.Log($"   Mağaza Filtresi: '{storeFilter}'");
        if (!string.IsNullOrWhiteSpace(categoryFilter))
            Logger.Log($"   Kategori Filtresi: '{categoryFilter}'");
        Logger.Log($"=========================================================");

        using var db = new SqlConnection(_connectionString);
        await db.OpenAsync();

        // Ensure newly discovered categories are mapped first
        await CategoryResolver.AlignUnmappedShopCategoriesAsync(db);

        var query = @"
            SELECT 
                s.Name AS StoreName,
                sc.VendorExternalId AS StoreCategoryId,
                cm.CanonicalCategoryId AS TargetCategoryId,
                sc.RawPath AS CategoryPath,
                c.Name AS CanonicalCategoryName
            FROM dbo.CategoryMappings cm
            JOIN dbo.ShopCategories sc ON cm.ShopCategoryId = sc.Id
            JOIN dbo.Sellers s ON sc.ShopId = s.Id
            JOIN dbo.Categories c ON cm.CanonicalCategoryId = c.Id
            WHERE cm.ConfidenceScore >= 0.65 
              AND cm.MappedBy <> 'AUTO_PENDING'
              AND sc.VendorExternalId IS NOT NULL
              AND s.IsActive = 1";

        var mappings = (await db.QueryAsync<StoreCategoryMappingDto>(query)).ToList();

        if (!string.IsNullOrWhiteSpace(storeFilter))
        {
            mappings = mappings.Where(m => m.StoreName.Contains(storeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            mappings = mappings.Where(m => 
                (m.CategoryPath != null && m.CategoryPath.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase)) ||
                (m.CanonicalCategoryName != null && m.CanonicalCategoryName.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase)) ||
                m.TargetCategoryId.ToString() == categoryFilter).ToList();
        }

        if (mappings.Count == 0)
        {
            Logger.Log($"[Bilgi] Eşleşen kategori bulunamadı (Filtreler: Store='{storeFilter}', Cat='{categoryFilter}').");
            return;
        }

        Logger.Log($"[Bilgi] Toplam {mappings.Count} adet haritalanmış kategori dalı taranacak.");
        int totalSavedAllCategories = 0;

        foreach (var mapping in mappings)
        {
            var targetScraper = _categoryScrapers.FirstOrDefault(s => s.Name.Equals(mapping.StoreName, StringComparison.OrdinalIgnoreCase));
            if (targetScraper == null)
            {
                Logger.Log($"[Atlandı] '{mapping.StoreName}' için ICategoryScraper bulunamadı.");
                continue;
            }

            int storeProductCount = await db.QuerySingleAsync<int>(@"
                SELECT COUNT(1) 
                FROM Offers o
                JOIN Products p ON o.ProductId = p.Id
                JOIN Sellers s ON o.SellerId = s.Id
                WHERE p.CategoryId = @CategoryId AND LOWER(s.Name) = LOWER(@StoreName)",
                new { CategoryId = mapping.TargetCategoryId, StoreName = mapping.StoreName });

            if (storeProductCount >= maxProductsPerCategory)
            {
                Logger.Log($"[Kategori Dolu] [{mapping.CanonicalCategoryName}] (ID: {mapping.TargetCategoryId}) | {mapping.StoreName}: Bu mağazadan zaten {storeProductCount} ürün var.");
                continue;
            }

            int needed = maxProductsPerCategory - storeProductCount;
            Logger.Log($"\n>>> [{mapping.CanonicalCategoryName} (ID: {mapping.TargetCategoryId})] {mapping.StoreName.ToUpper()} -> '{mapping.CategoryPath}' ({mapping.StoreCategoryId})");
            Logger.Log($"    Mağaza Mevcut: {storeProductCount} | Hedeflenen Ek: {needed}");

            var specScraper = targetScraper as IProductSpecScraper;
            int categorySavedCount = 0;

            try
            {
                var fetched = await targetScraper.FetchProductsByCategoryAsync(mapping.StoreCategoryId);
                if (fetched.Count == 0)
                {
                    Logger.Log($"    [Uyarı] '{mapping.StoreCategoryId}' için {mapping.StoreName} hiç ürün döndürmedi.");
                    continue;
                }

                int maxToTake = Math.Max(0, maxProductsPerCategory - (storeProductCount + categorySavedCount));
                var slice = fetched.Take(maxToTake).ToList();
                var toMatch = await _repo.SaveBatchAsync(targetScraper.Name, slice);

                if (toMatch.Count > 0)
                {
                    var matchResults = await _matchingService.MatchProductsBatchAsync(
                        toMatch, targetScraper.Name, specScraper, forcedCategoryId: mapping.TargetCategoryId);
                    int newlySaved = matchResults.Count(m => m);
                    categorySavedCount += newlySaved;
                    totalSavedAllCategories += newlySaved;

                    Logger.Log($"    [{targetScraper.Name}] '{mapping.CategoryPath}' kategorisinden {newlySaved} ürün başarıyla kaydedildi/eşleşti.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"    [{targetScraper.Name}] '{mapping.StoreCategoryId}' çekilirken hata: {ex.Message}");
            }
        }

        // Run alignment sync to heal any newly ingested product rows
        await CategoryResolver.AlignUnmappedShopCategoriesAsync(db);

        Logger.Log($"\n=========================================================");
        Logger.Log($"   DOĞRUDAN KATEGORİ TARAMA TAMAMLANDI!                  ");
        Logger.Log($"   Bu oturumda eklenen/güncellenen ürün sayısı: {totalSavedAllCategories}");
        Logger.Log($"=========================================================");
    }
}

