using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PriceAggregator.Core;
using PriceAggregator.Core.Categorization;

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var connectionString = config.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");



var migrosScraper = new MigrosScraper(new HttpClient());
var macroCenterScraper = new MacroCenterScraper(new HttpClient());
var marketFiyatiScraper = new MarketFiyatiScraper(new HttpClient());
var hakmarExpressScraper = new HakmarExpressScraper(new HttpClient());
var cagriMarketScraper = new CagriMarketScraper(new HttpClient());
var evideaScraper = new EvideaScraper(new HttpClient());
var mionScraper = new MionScraper(new HttpClient());
var ikeaScraper = new IkeaScraper(new HttpClient());
var teknosaScraper = new TeknosaScraper(new HttpClient());
var avansasScraper = new AvansasScraper(new HttpClient());
var a101Scraper = new A101Scraper(new HttpClient());

var repo = new ProductRepository(connectionString);
var matchingService = new MatchingService(connectionString);

// `dotnet run -- --backfill-only` skips the fixed search-term/cosmetics sweep below and goes
// straight to SpecsBackfillJob — useful for topping up specs coverage alone (e.g. after
// resetting a batch of rate-limited products) without re-running the whole catalog scrape.
var backfillOnly = args.Contains("--backfill-only");
var teknosaOnly = args.Contains("--run-teknosa") || args.Contains("--teknosa-only");

if (args.Contains("--run-a101"))
{
    Logger.Log("========================================");
    Logger.Log("     RUN A101 SCRAPER TEST              ");
    Logger.Log("========================================");
    var a101Products = await a101Scraper.FetchProductsAsync("süt");
    Logger.Log($"[A101] 'süt' araması sonucu çekilen ürün sayısı: {a101Products.Count}");
    foreach (var p in a101Products.Take(5))
    {
        Logger.Log($" - {p.Title} | Fiyat: {p.Price} TL | Marka: {p.Brand} | URL: {p.ProductUrl}");
    }
    return;
}

if (args.Contains("--scrape-a101"))
{
    Logger.Log("========================================");
    Logger.Log("     SCRAPING A101 PRODUCTS INTO DB     ");
    Logger.Log("========================================");
    var a101List = new List<IProductScraper> { a101Scraper };
    var categoryJob = new CategoryBasedScrapeJob(connectionString, matchingService, a101List);
    string txt = CategoryKeywordCatalog.FindTxtFilePath()
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "kategori_urun_listesi.txt");
    await categoryJob.RunAsync(txt, maxProductsPerCategory: 30, onlyUnmappedCategories: false);
    Logger.Log("[A101] Scrape and matching complete.");
    return;
}

if (args.Contains("--check-matches"))
{
    Logger.Log("========================================");
    Logger.Log("     CHECK PRODUCT MATCHES & SUSPECTS   ");
    Logger.Log("========================================");
    using var db = new SqlConnection(connectionString);
    await db.OpenAsync();
    var suspects = await ProductMatchReviewer.FindSuspectsAsync(db);
    Logger.Log($"[MatchReviewer] Toplam şüpheli eşleşme sayısı: {suspects.Count}");
    foreach (var s in suspects.Take(20))
    {
        Logger.Log($"[Suspect {s.OfferId} | Skor: {s.Score:F2}]");
        Logger.Log($"   Kanonik: [{s.CanonicalTitle}] (ID: {s.ProductId})");
        Logger.Log($"   Satıcı : [{s.ExternalTitle}] ({s.SellerName})");
    }
    return;
}

if (args.Contains("--fix-suspects"))
{
    Logger.Log("========================================");
    Logger.Log("     FIX SUSPECT PRODUCT MATCHES RUN    ");
    Logger.Log("========================================");
    using var db = new SqlConnection(connectionString);
    await db.OpenAsync();
    var outcomes = await ProductMatchReviewer.FixSuspectsAsync(db);
    Logger.Log($"[MatchReviewer] Toplam düzeltilen/ayrılan teklif sayısı: {outcomes.Count}");
    foreach (var o in outcomes)
    {
        Logger.Log($"Offer #{o.OfferId} ({o.SellerName}): {o.Action} -> Eski Ürün: {o.OldProductId}, Yeni Ürün: {o.NewProductId} | Başlık: {o.ExternalTitle}");
    }
    return;
}

if (args.Contains("--reindex"))
{
    Logger.Log("========================================");
    Logger.Log("     ELASTICSEARCH REINDEX RUN          ");
    Logger.Log("========================================");
    var esService = new PriceAggregator.Core.Search.ElasticsearchService(config);
    var reindexJob = new PriceAggregator.Core.Search.ElasticsearchReindexJob(connectionString, esService);
    var result = await reindexJob.RunAsync();
    Logger.Log($"[Elasticsearch] Reindex complete: {result.IndexedProducts}/{result.TotalProducts} products indexed.");
    return;
}

if (args.Contains("--cleanup-categories"))
{
    Logger.Log("========================================");
    Logger.Log("     TAXONOMY CLEANUP & REMAP RUN       ");


    Logger.Log("========================================");
    await TaxonomySanitizer.RemapAndPurgeRogueCategoriesAsync(connectionString);
    return;
}

if (args.Contains("--recategorize-all"))
{
    Logger.Log("========================================");
    Logger.Log("   FULL RE-CATEGORIZATION FROM SCRATCH  ");
    Logger.Log("========================================");
    var result = await ProductRecategorizer.RunAsync(connectionString);
    Logger.Log($"[Recategorizer] Sonuc: {result.Categorized} urun basariyla kategorilendi, {result.DeletedUnmatched} uymayan urun silindi.");
    return;
}

if (args.Contains("--backfill-tech-attrs"))
{
    Logger.Log("========================================");
    Logger.Log("   BACKFILL TECH ATTRIBUTES RUN         ");
    Logger.Log("========================================");
    using var db = new SqlConnection(connectionString);
    await db.OpenAsync();
    var products = (await db.QueryAsync<(int Id, string Name)>(
        "SELECT Id, Name FROM Products WHERE Name LIKE '%GB%' OR Name LIKE '%TB%' OR Name LIKE '%RAM%' OR Name LIKE '%SSD%'")).ToList();
    Logger.Log($"Toplam incelenecek aday ürün sayısı: {products.Count}");
    int addedCount = 0;
    foreach (var p in products)
    {
        var attrs = TechAttributeExtractor.Extract(p.Name);
        foreach (var (attrName, attrValue) in attrs)
        {
            var inserted = await db.ExecuteAsync(@"
                IF NOT EXISTS (SELECT 1 FROM ProductAttributes WHERE ProductId = @ProductId AND AttributeName = @Name AND AttributeValue = @Value)
                BEGIN
                    INSERT INTO ProductAttributes (ProductId, AttributeName, AttributeValue) VALUES (@ProductId, @Name, @Value)
                END", new { ProductId = p.Id, Name = attrName, Value = attrValue });
            if (inserted > 0)
            {
                addedCount++;
                Logger.Log($"[+{attrName}: {attrValue}] -> Product #{p.Id}: {p.Name}");
            }
        }
    }
    Logger.Log($"İşlem tamamlandı! Toplam eklenen teknoloji niteliği: {addedCount}");
    return;
}

if (args.Contains("--align-trees"))
{
    Logger.Log("========================================");
    Logger.Log("     CATEGORY TREE ALIGNMENT RUN        ");
    Logger.Log("========================================");
    using var db = new SqlConnection(connectionString);
    await db.OpenAsync();
    int aligned = await CategoryResolver.AlignUnmappedShopCategoriesAsync(db);
    Logger.Log($"[TreeAlignment] Successfully aligned {aligned} shop categories to canonical taxonomy.");
    return;
}

if (args.Contains("--test-hakmar"))
{
    Logger.Log("========================================");
    Logger.Log("     TEST HAKMAR SCRAPER RUN            ");
    Logger.Log("========================================");
    var categories = await hakmarExpressScraper.FetchStoreCategoriesAsync();
    Logger.Log($"[Hakmar] Fetched {categories.Count} categories from Hakmar Express.");
    var energyDrinks = await hakmarExpressScraper.FetchProductsByCategoryAsync("enerji-icecegi-c");
    Logger.Log($"[Hakmar] Fetched {energyDrinks.Count} products from 'enerji-icecegi-c'.");
    foreach (var p in energyDrinks)
    {
        Logger.Log($" - {p.Title} | Category: {p.CategoryName} | StoreCatId: {p.StoreCategoryId}");
    }
    var toMatch = await repo.SaveBatchAsync(hakmarExpressScraper.Name, energyDrinks);
    var matched = await matchingService.MatchProductsBatchAsync(toMatch, hakmarExpressScraper.Name);
    Logger.Log($"[Hakmar] Matched: {matched.Count(m => m)}/{energyDrinks.Count}");
    return;
}

if (teknosaOnly)
{
    Logger.Log("========================================");
    Logger.Log("     EXCLUSIVE TEKNOSA SCRAPER RUN      ");
    Logger.Log("========================================");

    var exclusiveTeknosaFetched = await teknosaScraper.FetchCategoryProductsAsync(maxCategories: int.MaxValue, maxProductsPerCategory: int.MaxValue);
    var exclusiveTeknosaToMatch = await repo.SaveBatchAsync(teknosaScraper.Name, exclusiveTeknosaFetched);
    if (exclusiveTeknosaToMatch.Count > 0)
    {
        var teknosaResults = await matchingService.MatchProductsBatchAsync(exclusiveTeknosaToMatch, teknosaScraper.Name, teknosaScraper);
        Logger.Log($"[Teknosa] {teknosaResults.Count(m => m)} ürün başarıyla kaydedildi/eşleştirildi.");
    }
    Logger.Log("[Teknosa] Exclusive category-based scrape run complete.");
    return;
}

var allScrapersList = new List<IProductScraper>
{
    migrosScraper, macroCenterScraper, marketFiyatiScraper, hakmarExpressScraper,
    cagriMarketScraper, evideaScraper, mionScraper, ikeaScraper, avansasScraper, a101Scraper
};

if (args.Contains("--reset-catalog"))
{
    Logger.Log("========================================");
    Logger.Log("     CATALOG FULL RESET RUN             ");
    Logger.Log("========================================");
    using var db = new SqlConnection(connectionString);
    await db.OpenAsync();
    await db.ExecuteAsync(@"
        DELETE FROM Favorites;
        DELETE FROM PurchaseHistory;
        DELETE FROM UserProductViews;
        DELETE FROM OffersPriceHistory;
        DELETE FROM ProductMatchingLogs;
        DELETE FROM Offers;
        DELETE FROM ProductAttributes;
        DELETE FROM ProductSpecs;
        DELETE FROM Products;
        DBCC CHECKIDENT ('Products', RESEED, 0);
        DBCC CHECKIDENT ('Offers', RESEED, 0);
    ");
    Logger.Log("[CatalogReset] Products, Offers, Attributes ve Fiyat Geçmişi başarıyla sıfırlandı. Sellers ve ShopCategories korundu.");
    return;
}

// 1. Doğrudan Kategori Ağacı Taraması (Primary: Direct Category Tree Scraping)
Logger.Log("=========================================================");
Logger.Log("   1. AŞAMA: DOĞRUDAN KATEGORİ AĞACI TARAMASI BAŞLATILIYOR ");
Logger.Log("=========================================================");
var mappedJob = new MappedCategoryScrapeJob(connectionString, matchingService, allScrapersList);
await mappedJob.RunAsync(maxProductsPerCategory: 30);

// 2. Kelime Bazlı Tarama (Kalan Mağazalar & Kategoriler İçin: A101, Evidea, Ikea, Çağrı, MarketFiyatı)
Logger.Log("=========================================================");
Logger.Log("   2. AŞAMA: TÜM MAĞAZALAR İÇİN KELİME BAZLI TARAMA      ");
Logger.Log("=========================================================");
var categoryScrapeJob = new CategoryBasedScrapeJob(connectionString, matchingService, allScrapersList);
string txtPath = CategoryKeywordCatalog.FindTxtFilePath()
    ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "kategori_urun_listesi.txt");

await categoryScrapeJob.RunAsync(txtPath, maxProductsPerCategory: 30, onlyUnmappedCategories: false);

// Avansas Category Traversal
Logger.Log("--- Scraping Avansas (category traversal - max 30 per category) ---");
try
{
    var avansasFetched = await avansasScraper.FetchCategoryProductsAsync(maxCategories: 50, maxProductsPerCategory: 30);
    var avansasToMatch = await repo.SaveBatchAsync(avansasScraper.Name, avansasFetched);
    if (avansasToMatch.Count > 0)
    {
        var avansasResults = await matchingService.MatchProductsBatchAsync(avansasToMatch, avansasScraper.Name, avansasScraper);
        Logger.Log($"[Avansas] {avansasResults.Count(m => m)} ürün başarıyla kaydedildi/eşleştirildi.");
    }
}
catch (Exception ex)
{
    Logger.Log($"[Avansas] Kategori tarama hatası: {ex.Message}");
}

// Teknosa Category Traversal
Logger.Log("--- Scraping Teknosa (category traversal - max 30 per category) ---");
try
{
    var teknosaFetched = await teknosaScraper.FetchCategoryProductsAsync(maxCategories: int.MaxValue, maxProductsPerCategory: 30);
    var teknosaToMatch = await repo.SaveBatchAsync(teknosaScraper.Name, teknosaFetched);
    if (teknosaToMatch.Count > 0)
    {
        var teknosaResults = await matchingService.MatchProductsBatchAsync(teknosaToMatch, teknosaScraper.Name, teknosaScraper);
        Logger.Log($"[Teknosa] {teknosaResults.Count(m => m)} ürün başarıyla kaydedildi/eşleştirildi.");
    }
}
catch (Exception ex)
{
    Logger.Log($"[Teknosa] Kategori tarama hatası: {ex.Message}");
}


// The loops above only touch products that happen to match one of the fixed search terms
// (or a swept cosmetics category) — most of the existing catalog was scraped under different,
// no-longer-searched terms and would otherwise sit with SpecsFetchedAt = NULL forever. This
// walks every remaining product directly so a single run actually reaches full coverage; see
// SpecsBackfillJob for why the fixed-term loops alone can't guarantee that.
Logger.Log("--- Backfilling specs for remaining products (full catalog) ---");
var allScrapers = new IProductScraper[]
{
    migrosScraper, macroCenterScraper, marketFiyatiScraper, hakmarExpressScraper,
    cagriMarketScraper, evideaScraper, mionScraper, ikeaScraper, teknosaScraper, avansasScraper
};
var specsBackfillJob = new SpecsBackfillJob(connectionString, allScrapers);
var backfillResult = await specsBackfillJob.RunAsync();
Logger.Log($"[SpecsBackfill] Done. Processed: {backfillResult.Processed}, with specs: {backfillResult.WithSpecs}, failed: {backfillResult.Failed}");