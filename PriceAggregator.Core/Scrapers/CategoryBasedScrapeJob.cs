using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core.Data;
using PriceAggregator.Core.Matching;

namespace PriceAggregator.Core.Scrapers;

public class CategoryBasedScrapeJob
{
    private readonly string _connectionString;
    private readonly ProductRepository _repo;
    private readonly MatchingService _matchingService;
    private readonly List<IProductScraper> _allScrapers;

    public CategoryBasedScrapeJob(string connectionString, MatchingService matchingService, IEnumerable<IProductScraper> scrapers)
    {
        _connectionString = connectionString;
        _repo = new ProductRepository(connectionString);
        _matchingService = matchingService;
        _allScrapers = scrapers.ToList();
    }

    public async Task RunAsync(
        string txtFilePath, 
        int maxProductsPerCategory = 300, 
        bool onlyMappedCategories = false,
        string? categoryFilter = null,
        int? maxCategories = null,
        bool onlyUnmappedCategories = true)
    {
        Logger.Log($"=========================================================");
        Logger.Log($"   KATEGORİ BAZLI ÜRÜN ÇEKME BAŞLATILIYOR               ");
        Logger.Log($"   Hedef: Her kategoriden en fazla {maxProductsPerCategory} ürün");
        if (onlyUnmappedCategories)
            Logger.Log($"   Mod: EŞLEŞMEYEN KATEGORİLER İÇİN FALLBACK (Yalnızca Mapping'de Olmayanlar)");
        if (onlyMappedCategories)
            Logger.Log($"   Mod: YALNIZCA HARİTALANMIŞ KATEGORİLER (Mapped Only)");
        if (!string.IsNullOrWhiteSpace(categoryFilter))
            Logger.Log($"   Filtre: '{categoryFilter}'");
        if (maxCategories.HasValue)
            Logger.Log($"   Limit: En fazla {maxCategories.Value} kategori");
        Logger.Log($"=========================================================");

        if (!File.Exists(txtFilePath))
        {
            Logger.Log($"[HATA] Kategori listesi dosyası bulunamadı: {txtFilePath}");
            return;
        }

        using var db = new SqlConnection(_connectionString);
        await db.OpenAsync();

        var dbCategories = (await db.QueryAsync<(int Id, string Name, int? ParentCategoryId, int? SectorId)>(
            "SELECT Id, Name, ParentCategoryId, SectorId FROM Categories")).ToList();

        var targets = CategoryKeywordCatalog.ParseFile(txtFilePath, dbCategories);

        HashSet<int>? mappedCategoryIds = null;
        if (onlyUnmappedCategories)
        {
            // CategoryMappings ve ShopCategories üzerinden aktif haritalanmış canonical kategori ID'leri
            mappedCategoryIds = (await db.QueryAsync<int>(@"
                SELECT DISTINCT cm.CanonicalCategoryId
                FROM dbo.CategoryMappings cm
                JOIN dbo.ShopCategories sc ON cm.ShopCategoryId = sc.Id
                JOIN dbo.Sellers s ON sc.ShopId = s.Id
                WHERE cm.ConfidenceScore >= 0.65 
                  AND cm.MappedBy <> 'AUTO_PENDING'
                  AND sc.VendorExternalId IS NOT NULL
                  AND s.IsActive = 1
                UNION
                SELECT DISTINCT TargetCategoryId
                FROM dbo.SellerCategories
                WHERE IsActive = 1 AND TargetCategoryId IS NOT NULL")).ToHashSet();

            Logger.Log($"[Unmapped Modu] Toplam {mappedCategoryIds.Count} kategori zaten mağaza ağaçlarıyla eşleşmiş. Yalnızca eşleşmeyen kategoriler için anahtar kelime araması yapılacak.");
        }

        if (onlyMappedCategories)
        {
            var mappedTargetCategoryIds = (await db.QueryAsync<int>(
                "SELECT DISTINCT TargetCategoryId FROM SellerCategories WHERE IsActive = 1 AND TargetCategoryId IS NOT NULL")).ToHashSet();

            targets = targets.Where(t => mappedTargetCategoryIds.Contains(t.CategoryId)).ToList();
            Logger.Log($"[Mapped Categories Filter] Yalnızca SellerCategories tablosunda aktif eşlenmiş {targets.Count} adet kategori taranacak.");
        }
        else
        {
            Logger.Log($"[Bilgi] Toplam {targets.Count} adet yaprak (Tier 3) kategori okundu.");
        }

        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            targets = targets.Where(t => 
                t.LeafCategory.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase) ||
                t.SubCategory.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase) ||
                t.CategoryId.ToString() == categoryFilter).ToList();
            Logger.Log($"[Kategori Filtresi Uygulandı] '{categoryFilter}' filtresiyle {targets.Count} kategori eşleşti.");
        }

        if (maxCategories.HasValue && maxCategories.Value > 0)
        {
            targets = targets.Take(maxCategories.Value).ToList();
            Logger.Log($"[Kategori Limiti Uygulandı] En fazla {targets.Count} kategori taranacak.");
        }

        int totalSavedAllCategories = 0;

        foreach (var target in targets)
        {
            if (onlyUnmappedCategories && mappedCategoryIds != null && mappedCategoryIds.Contains(target.CategoryId))
            {
                Logger.Log($"[Kategori Atlandı] [{target.RootCategory} -> {target.SubCategory} -> {target.LeafCategory}] (ID: {target.CategoryId}): Kategori mappingde eşleşmiş olduğu için doğrudan mağaza taraması kullanılıyor, kelime araması atlanıyor.");
                continue;
            }

            int currentDbCount = await db.QuerySingleAsync<int>(
                "SELECT COUNT(1) FROM Products WHERE CategoryId = @CategoryId",
                new { CategoryId = target.CategoryId });

            Logger.Log($"\n>>> [{target.RootCategory} -> {target.SubCategory} -> {target.LeafCategory}] (ID: {target.CategoryId}) [Eşleşmemiş Kategori - Kelime Araması]");
            Logger.Log($"    Mevcut DB Ürün: {currentDbCount} | Arama Kelimeleri: {string.Join(", ", target.Keywords)}");

            int categorySavedCount = 0;

            foreach (var keyword in target.Keywords)
            {
                if (categorySavedCount >= maxProductsPerCategory)
                    break;

                var scrapersToRun = SelectScrapersForCategory(target.RootCategory, target.SubCategory, keyword);

                if (onlyMappedCategories)
                {
                    var mappedSellerNames = (await db.QueryAsync<string>(@"
                        SELECT LOWER(s.Name) 
                        FROM SellerCategories sc
                        JOIN Sellers s ON sc.SellerId = s.Id
                        WHERE sc.TargetCategoryId = @CategoryId AND sc.IsActive = 1",
                        new { CategoryId = target.CategoryId })).ToHashSet();

                    if (mappedSellerNames.Count > 0)
                    {
                        var filtered = _allScrapers.Where(s => mappedSellerNames.Contains(s.Name.ToLowerInvariant())).ToList();
                        if (filtered.Count > 0)
                        {
                            scrapersToRun = filtered;
                        }
                    }
                }

                foreach (var scraper in scrapersToRun)
                {
                    if (categorySavedCount >= maxProductsPerCategory)
                        break;

                    var scraperName = scraper.Name;
                    var specScraper = scraper as IProductSpecScraper;

                    try
                    {
                        var fetched = await scraper.FetchProductsAsync(keyword);
                        if (fetched.Count == 0) continue;

                        var eligibleProducts = new List<ProductDto>();
                        foreach (var product in fetched)
                        {
                            if (currentDbCount + categorySavedCount + eligibleProducts.Count >= maxProductsPerCategory)
                                break;

                            // 1. Strict Title Relevance Filter: ensure the product title actually matches the target category!
                            if (!CategoryKeywordCatalog.IsTitleRelevant(product.Title, target.Keywords))
                            {
                                continue;
                            }

                            // 2. Source Category Compatibility Filter: ensure the store's own category matches our target category!
                            if (!CategoryKeywordCatalog.IsSourceCompatible(product, target))
                            {
                                Logger.Log($"    [Kategori Uyuşmazlığı Engellendi] '{product.Title}' (Market Kategorisi: '{product.RootCategoryName} > {product.ParentCategoryName} > {product.CategoryName}') -> Hedef Kategori: '{target.RootCategory} > {target.SubCategory} > {target.LeafCategory}'");
                                continue;
                            }

                            eligibleProducts.Add(product);
                        }

                        var toMatch = await _repo.SaveBatchAsync(scraperName, eligibleProducts);

                        if (toMatch.Count > 0)
                        {
                            var matchResults = await _matchingService.MatchProductsBatchAsync(toMatch, scraperName, specScraper, forcedCategoryId: target.CategoryId);
                            int newlySaved = matchResults.Count(m => m);
                            categorySavedCount += newlySaved;
                            totalSavedAllCategories += newlySaved;

                            Logger.Log($"    [{scraperName}] '{keyword}' aramasıyla {newlySaved} ürün başarıyla kaydedildi/kategorilendi. (Kategori Toplamı: {currentDbCount + categorySavedCount}/{maxProductsPerCategory})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"    [{scraperName}] '{keyword}' araması esnasında hata: {ex.Message}");
                    }
                }
            }

            Logger.Log($"[Tamamlandı] {target.LeafCategory}: Toplam {currentDbCount + categorySavedCount} ürün veritabanında mevcut.");

            if (categorySavedCount > 0)
            {
                await db.ExecuteAsync(@"
                    UPDATE sc
                    SET sc.ProductCount = (SELECT COUNT(1) FROM Products p JOIN Offers o ON o.ProductId = p.Id WHERE p.CategoryId = sc.TargetCategoryId AND o.SellerId = sc.SellerId),
                        sc.LastScrapedAt = GETDATE()
                    FROM SellerCategories sc
                    WHERE sc.TargetCategoryId = @CategoryId",
                    new { CategoryId = target.CategoryId });
            }
        }

        Logger.Log($"\n=========================================================");
        Logger.Log($"   KATEGORİ BAZLI TARAMA TAMAMLANDI!                     ");
        Logger.Log($"   Bu oturumda eklenen/güncellenen ürün sayısı: {totalSavedAllCategories}");
        Logger.Log($"=========================================================");
    }

    private List<IProductScraper> SelectScrapersForCategory(string rootCat, string subCat, string keyword)
    {
        string rootLower = rootCat.ToLower();
        string subLower = subCat.ToLower();

        // Mobilya ve Hırdavat
        if (rootLower.Contains("mobilya") || subLower.Contains("mobilya"))
        {
            return _allScrapers.Where(s => s is EvideaScraper || s is IkeaScraper || s is MarketFiyatiScraper || s is MionScraper).ToList();
        }

        // Teknoloji
        if (rootLower.Contains("teknoloji") || subLower.Contains("teknoloji") || subLower.Contains("bilgisayar") || subLower.Contains("telefon"))
        {
            return _allScrapers.Where(s => s is MarketFiyatiScraper || s is EvideaScraper || s is MigrosScraper || s is AvansasScraper).ToList();
        }

        // Ofis ve Kırtasiye
        if (rootLower.Contains("ofis") || rootLower.Contains("kırtasiye") || subLower.Contains("kalem") || subLower.Contains("defter"))
        {
            return _allScrapers.Where(s => s is MarketFiyatiScraper || s is MigrosScraper || s is EvideaScraper || s is IkeaScraper || s is AvansasScraper).ToList();
        }

        // Temizlik ve Kişisel Bakım
        if (rootLower.Contains("temizlik") || rootLower.Contains("sağlık") || subLower.Contains("kozmetik") || subLower.Contains("kişisel"))
        {
            return _allScrapers.Where(s => s is MarketFiyatiScraper || s is MionScraper || s is MigrosScraper || s is MacroCenterScraper).ToList();
        }

        // Gıda ve Mutfak / Varsayılan
        return _allScrapers;
    }
}
