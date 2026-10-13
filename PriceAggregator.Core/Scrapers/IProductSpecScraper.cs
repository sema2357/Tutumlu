using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Scrapers;

// Optional add-on a scraper implements when its store exposes a real per-product detail
// page/API beyond the search-listing JSON (nutrition facts, ingredients, dimensions, ...).
// Deliberately separate from IProductScraper rather than a required member: most scrapers only
// have the listing shape, and MatchingService probes for this via an `is` check (see
// MatchingService.MatchProductAsync) so sources without it are simply skipped, not broken.
//
// Takes the full ProductDto rather than a bare id because some sources' detail endpoints key
// off something other than the numeric ExternalId (e.g. HakmarExpressScraper needs the slug
// embedded in ProductUrl).
public interface IProductSpecScraper
{
    Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product);
}
