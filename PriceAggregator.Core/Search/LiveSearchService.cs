using PriceAggregator.Core.Scrapers;

namespace PriceAggregator.Core.Search;

public class LiveSearchService
{
    private readonly IEnumerable<IProductScraper> _scrapers;
    private readonly ProductRepository _repo;
    private readonly MatchingService _matchingService;

    public LiveSearchService(IEnumerable<IProductScraper> scrapers, ProductRepository repo, MatchingService matchingService)
    {
        _scrapers = scrapers;
        _repo = repo;
        _matchingService = matchingService;
    }

    public async Task ExecuteAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return;

        var queryTasks = _scrapers.Select(async scraper =>
        {
            try
            {
                var specScraper = scraper as IProductSpecScraper;
                var scrapedProducts = await scraper.FetchProductsAsync(searchTerm);
                var searchTermAsKeyword = new List<string> { searchTerm };
                foreach (var product in scrapedProducts)
                {
                    // We trust the store's search engine to return relevant results for a manual search.
                    // Strict IsTitleRelevant filtering is skipped here because users might search for
                    // specific brands or models not cataloged in our keyword list.

                    var savedId = await _repo.SaveAsync(scraper.Name, product);
                    if (savedId is not null)
                    {
                        await _matchingService.MatchProductAsync(savedId.Value, product, scraper.Name, specScraper);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[LiveSearch] Scraper {scraper.Name} failed: {ex.Message}");
            }
        });

        // Enforce a maximum timeout so the API doesn't hang indefinitely
        var allScrapersTask = Task.WhenAll(queryTasks);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));

        await Task.WhenAny(allScrapersTask, timeoutTask);
    }
}
