using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Scrapers;

// Catches up every Product whose specs were never fetched, independent of the fixed
// search-term lists Worker/PriceAggregator.Scraper cycle through. MatchingService only fetches
// specs for a product when one of its offers happens to get rescraped (see
// MatchingService.UpsertSpecsAsync) — a product bought in under an older search term, or one
// none of the standing search terms ever touch again, would otherwise sit with
// SpecsFetchedAt = NULL forever. This walks Products directly instead, so it's the only path
// that guarantees full catalog coverage. Safe to re-run: already-fetched products are excluded
// by the same SpecsFetchedAt gate.
public class SpecsBackfillJob
{
    private readonly string _connectionString;
    private readonly Dictionary<string, IProductSpecScraper> _specScrapersBySeller;

    public SpecsBackfillJob(string connectionString, IEnumerable<IProductScraper> scrapers)
    {
        _connectionString = connectionString;
        _specScrapersBySeller = scrapers
            .Where(s => s is IProductSpecScraper)
            .ToDictionary(s => s.Name, s => (IProductSpecScraper)s, StringComparer.OrdinalIgnoreCase);
    }

    public record Result(int Processed, int WithSpecs, int Failed);

    public async Task<Result> RunAsync(int? maxProducts = null, CancellationToken ct = default)
    {
        using var db = new SqlConnection(_connectionString);

        if (_specScrapersBySeller.Count == 0)
            return new Result(0, 0, 0);

        // One representative offer per still-unfetched product, from whichever supported
        // seller has one — a product can be sold by several sellers, only one detail page
        // needs to be read to get its specs.
        var targets = (await db.QueryAsync<BackfillTarget>(@"
            ;WITH Candidate AS (
                SELECT p.Id AS ProductId, s.Name AS SellerName, sp.SellerProductCode, sp.ExternalUrl,
                       ROW_NUMBER() OVER (PARTITION BY p.Id ORDER BY o.Id) AS Rn
                FROM Products p
                JOIN Offers o ON o.ProductId = p.Id
                JOIN Sellers s ON s.Id = o.SellerId
                JOIN SellerProducts sp ON sp.Id = o.SellerProductId
                WHERE p.SpecsFetchedAt IS NULL AND s.Name IN @SellerNames
            )
            SELECT ProductId, SellerName, SellerProductCode, ExternalUrl FROM Candidate WHERE Rn = 1",
            new { SellerNames = _specScrapersBySeller.Keys.ToList() })).ToList();

        if (maxProducts is > 0)
            targets = targets.Take(maxProducts.Value).ToList();

        int processed = 0, withSpecs = 0, failed = 0;

        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) break;
            if (!_specScrapersBySeller.TryGetValue(target.SellerName, out var specScraper)) continue;
            if (!long.TryParse(target.SellerProductCode, out var externalId)) continue;

            var dto = new ProductDto { ExternalId = externalId, ProductUrl = target.ExternalUrl ?? "", Title = "" };

            List<ProductSpec> specs;
            try
            {
                specs = await specScraper.FetchSpecsAsync(dto);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SpecsBackfill] Failed to fetch specs for product {target.ProductId} ({target.SellerName}): {ex.Message}");
                failed++;
                continue;
            }

            foreach (var spec in specs)
            {
                try
                {
                    await db.ExecuteAsync(
                        @"IF NOT EXISTS (SELECT 1 FROM ProductSpecs WHERE ProductId = @ProductId AND SpecGroup = @Group AND SpecName = @Name)
                          INSERT INTO ProductSpecs (ProductId, SpecGroup, SpecName, SpecValue) VALUES (@ProductId, @Group, @Name, @Value)",
                        new { ProductId = target.ProductId, Group = spec.Group, Name = spec.Name, Value = spec.Value });
                }
                catch (SqlException)
                {
                    // Unique constraint hit — a concurrent run already wrote this exact row.
                }
            }

            await db.ExecuteAsync("UPDATE Products SET SpecsFetchedAt = GETDATE() WHERE Id = @Id", new { Id = target.ProductId });

            processed++;
            if (specs.Count > 0) withSpecs++;

            // Paced well below the per-product delay the regular scrape uses: this job hits the
            // same handful of detail endpoints back-to-back for potentially thousands of
            // products in one run, and MacroCenter has already been observed rate-limiting
            // (HTTP 429) under that kind of burst.
            try { await Task.Delay(400, ct); } catch (TaskCanceledException) { break; }
        }

        Logger.Log($"[SpecsBackfill] Processed: {processed}, with specs: {withSpecs}, failed: {failed}, remaining candidates skipped by maxProducts: {Math.Max(0, targets.Count - processed - failed)}");
        return new Result(processed, withSpecs, failed);
    }

    private record BackfillTarget(int ProductId, string SellerName, string SellerProductCode, string? ExternalUrl);
}
