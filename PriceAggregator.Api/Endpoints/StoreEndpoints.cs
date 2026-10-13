using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;

namespace PriceAggregator.Api.Endpoints;

public static class StoreEndpoints
{
    public record StoreFacetDto(string Name, string DisplayName, int ProductCount);

    private static string FormatStoreName(string raw) => raw.ToLowerInvariant() switch
    {
        "migros" => "Migros",
        "macrocenter" => "Macrocenter",
        "hakmarexpress" or "hakmar express" or "hakmar" => "Hakmar Express",
        "cagrimarket" or "cagri market" => "Çağrı Market",
        "evidea" => "Evidea",
        "mion" => "Mion",
        "ikea" => "IKEA",
        "teknosa" => "Teknosa",
        "avansas" => "Avansas",
        "marketfiyati" or "market fiyati" => "Market Fiyatı",
        "a101" => "A101",
        _ => raw
    };

    public static void MapStoreEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/stores", async (
            string? category,
            string? q,
            string[]? brand,
            string[]? attr,
            decimal? minPrice,
            decimal? maxPrice,
            bool? onSale,
            ICacheService cache) =>
        {
            async Task<List<StoreFacetDto>> FetchAsync()
            {
                using var db = new SqlConnection(connectionString);
                var (whereClause, parameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
                ProductFilterHelpers.AppendBrandFilter(brand, ref whereClause, parameters);
                ProductFilterHelpers.AppendAttrGroupFilters(ProductFilterHelpers.ParseAttrGroups(attr), ref whereClause, parameters);

                var sql = $@"
                    SELECT s.Name, COUNT(DISTINCT p.Id) AS ProductCount
                    FROM Sellers s
                    JOIN Offers o ON o.SellerId = s.Id
                    JOIN Products p ON p.Id = o.ProductId
                    {whereClause}
                    AND s.IsActive = 1
                    GROUP BY s.Name
                    HAVING COUNT(DISTINCT p.Id) > 0
                    ORDER BY ProductCount DESC, s.Name ASC";

                var rows = await db.QueryAsync<(string Name, int ProductCount)>(sql, parameters, commandTimeout: 30);
                return rows.Select(r => new StoreFacetDto(r.Name, FormatStoreName(r.Name), r.ProductCount)).ToList();
            }

            var hasNarrowingFilters = !string.IsNullOrWhiteSpace(q) || (brand?.Length > 0) || (attr?.Length > 0) || minPrice.HasValue || maxPrice.HasValue || onSale == true;
            if (!hasNarrowingFilters)
            {
                var cacheKey = $"stores_{category ?? "all"}";
                var result = await cache.GetOrSetAsync(cacheKey, TimeSpan.FromMinutes(2), FetchAsync);
                return Results.Ok(result ?? new List<StoreFacetDto>());
            }

            return Results.Ok(await FetchAsync());
        });
    }
}
