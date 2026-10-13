// These five folders are all part of the same tightly-coupled domain model (e.g.
// CosmeticsScrapeJob in Scrapers/ needs Data/ProductRepository and Matching/MatchingService,
// CategoryResolver in Categorization/ needs Data/ProductDto) — the split below is for
// navigability (see the repo's README/structure notes), not for enforcing isolation between
// them, so every file in Core can see every other sub-namespace without a per-file using list.
global using PriceAggregator.Core.Scrapers;
global using PriceAggregator.Core.Categorization;
global using PriceAggregator.Core.Matching;
global using PriceAggregator.Core.Pricing;
global using PriceAggregator.Core.Data;
global using PriceAggregator.Core.Helpers;
global using PriceAggregator.Core.Search;
global using PriceAggregator.Core.BackgroundJobs;
