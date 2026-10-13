using Microsoft.Extensions.Configuration;
using PriceAggregator.Core;
using PriceAggregator.Core.Categorization;
using PriceAggregator.Core.Data;
using PriceAggregator.Core.Matching;
using PriceAggregator.Core.Notifications;
using PriceAggregator.Core.Scrapers;

namespace PriceAggregator.Worker;

public class Worker : BackgroundService
{
    private readonly string _connectionString;
    private readonly TimeSpan _interval;
    private readonly int _maxProductsPerCategory;
    private readonly IEnumerable<IProductScraper> _scrapers;

    public Worker(IEnumerable<IProductScraper> scrapers, IConfiguration configuration)
    {
        _scrapers = scrapers;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        _maxProductsPerCategory = configuration.GetValue<int?>("WorkerSettings:MaxProductsPerCategory") ?? 400;

        var minutes = configuration.GetValue<int?>("WorkerSettings:IntervalMinutes");
        if (minutes.HasValue && minutes.Value > 0)
        {
            _interval = TimeSpan.FromMinutes(minutes.Value);
        }
        else
        {
            var hours = configuration.GetValue<int?>("WorkerSettings:IntervalHours") ?? 6;
            _interval = TimeSpan.FromHours(hours);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Logger.Log("=== Starting scheduled category-based scrape ===");
            try
            {
                await RunScrapeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Worker Error] Scheduled scrape failed: {ex.Message}");
            }

            Logger.Log($"=== Scrape finished. Waiting {_interval.TotalMinutes:0}m until next run. ===");

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Happens on graceful shutdown — expected, not an error
                break;
            }
        }
    }

    private async Task RunScrapeAsync(CancellationToken stoppingToken)
    {
        var repo = new ProductRepository(_connectionString);
        var matchingService = new MatchingService(_connectionString);

        // 1. Doğrudan Kategori Ağacı Taraması (Category Tree Scraping)
        // Mağazaların kendi kategori sayfalarından ürünleri çeker, kategori ağaçlarını eşleştirir.
        var mappedCategoryJob = new MappedCategoryScrapeJob(_connectionString, matchingService, _scrapers);
        try
        {
            Logger.Log("[Worker] Starting MappedCategoryScrapeJob (Direct Category Tree Ingestion)...");
            await mappedCategoryJob.RunAsync(maxProductsPerCategory: _maxProductsPerCategory);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Worker] Error in MappedCategoryScrapeJob: {ex.Message}");
        }

        if (stoppingToken.IsCancellationRequested) return;

        // 2. Kategori bazlı arama (kategori_urun_listesi.txt üzerinden kalan kategorileri tarar)
        var keywordScrapers = _scrapers.Where(s => s is not TeknosaScraper).ToList();
        var categoryScrapeJob = new CategoryBasedScrapeJob(_connectionString, matchingService, keywordScrapers);

        var txtPath = CategoryKeywordCatalog.FindTxtFilePath();
        if (!string.IsNullOrEmpty(txtPath) && File.Exists(txtPath))
        {
            Logger.Log($"[Worker] Running CategoryBasedScrapeJob (fallback for UNMAPPED categories)...");
            await categoryScrapeJob.RunAsync(txtPath, maxProductsPerCategory: _maxProductsPerCategory, onlyUnmappedCategories: true);
        }
        else
        {
            Logger.Log("[Worker] UYARI: kategori_urun_listesi.txt bulunamadı, kategori bazlı tarama atlanıyor.");
        }

        if (stoppingToken.IsCancellationRequested) return;

        // 2. Teknosa kategori bazlı doğrudan tarama
        var teknosaScraper = _scrapers.OfType<TeknosaScraper>().FirstOrDefault();
        if (teknosaScraper != null)
        {
            Logger.Log("--- Sweeping Teknosa (category browse) ---");
            try
            {
                var teknosaFetched = await teknosaScraper.FetchCategoryProductsAsync(maxCategories: 50, maxProductsPerCategory: 300);
                var teknosaToMatch = new List<(int SellerProductId, ProductDto Product)>();
                foreach (var product in teknosaFetched)
                {
                    var savedId = await repo.SaveAsync(teknosaScraper.Name, product);
                    if (savedId.HasValue) teknosaToMatch.Add((savedId.Value, product));
                }
                if (teknosaToMatch.Count > 0)
                {
                    var teknosaResults = await matchingService.MatchProductsBatchAsync(teknosaToMatch, teknosaScraper.Name, teknosaScraper);
                    Logger.Log($"[Teknosa] {teknosaResults.Count(m => m)} ürün başarıyla kaydedildi/eşleştirildi.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Teknosa] Error during sweep: {ex.Message}");
            }
        }

        if (stoppingToken.IsCancellationRequested) return;

        // 3. Besin değeri, alerjen ve teknik özellikleri eksik olan ürünleri güncelle (Specs backfill)
        Logger.Log("--- Backfilling specs for products ---");
        try
        {
            var specsBackfillJob = new SpecsBackfillJob(_connectionString, _scrapers);
            var backfillResult = await specsBackfillJob.RunAsync(maxProducts: 200, ct: stoppingToken);
            Logger.Log($"[SpecsBackfill] Done. Processed: {backfillResult.Processed}, with specs: {backfillResult.WithSpecs}, failed: {backfillResult.Failed}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[SpecsBackfill] Error: {ex.Message}");
        }

        // 4. Okunmuş eski bildirimleri temizle (kullanıcı başına en yeni 5 bildirim saklanır)
        Logger.Log("--- Cleaning up old read notifications ---");
        try
        {
            var notificationRepo = new NotificationRepository(_connectionString);
            await notificationRepo.CleanupOldReadNotificationsAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Notifications] Cleanup error: {ex.Message}");
        }
    }
}