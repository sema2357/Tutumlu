namespace PriceAggregator.Core.Scrapers;

// Groups scrapers by which search-term list Worker feeds them (grocery terms
// vs. furniture terms) — see Worker.RunScrapeAsync.
public enum ScraperSector
{
    Market,
    Mobilya,
}

// Common contract for every store scraper so Worker can iterate them
// polymorphically instead of calling each one out by name.
public interface IProductScraper
{
    string Name { get; }
    ScraperSector Sector { get; }
    Task<List<ProductDto>> FetchProductsAsync(string searchTerm);
}

// Interface for scrapers that can fetch products directly by store category ID (e.g., 'sebze-c-66')
public interface ICategoryScraper : IProductScraper
{
    Task<List<PriceAggregator.Core.Data.StoreCategoryDto>> FetchStoreCategoriesAsync();
    Task<List<PriceAggregator.Core.Data.ProductDto>> FetchProductsByCategoryAsync(string storeCategoryId);
}

