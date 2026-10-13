using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;

namespace PriceAggregator.Api.Endpoints;

public static class BrandEndpoints
{
    public record BrandFacetDto(string Name, int ProductCount);

    public static void MapBrandEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/brands", async (
            string? category, 
            string? q, 
            string[]? store, 
            string[]? attr, 
            decimal? minPrice, 
            decimal? maxPrice, 
            bool? onSale, 
            ICacheService cache) =>
        {
            async Task<List<BrandFacetDto>> FetchAsync()
            {
                using var db = new SqlConnection(connectionString);
                var (whereClause, parameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
                ProductFilterHelpers.AppendStoreFilter(store, ref whereClause, parameters);
                ProductFilterHelpers.AppendAttrGroupFilters(ProductFilterHelpers.ParseAttrGroups(attr), ref whereClause, parameters);

                var sql = $@"
                    SELECT b.Name, COUNT(DISTINCT p.Id) AS ProductCount
                    FROM Brands b
                    JOIN Products p ON p.BrandId = b.Id
                    {whereClause}
                    AND b.Name IS NOT NULL AND b.Name <> ''
                    GROUP BY b.Name
                    ORDER BY ProductCount DESC, b.Name ASC";

                return (await db.QueryAsync<BrandFacetDto>(sql, parameters, commandTimeout: 30)).ToList();
            }

            var hasNarrowingFilters = !string.IsNullOrWhiteSpace(q) || (store?.Length > 0) || (attr?.Length > 0) || minPrice.HasValue || maxPrice.HasValue || onSale == true;
            if (!hasNarrowingFilters)
            {
                var cacheKey = $"brands_{category ?? "all"}";
                var result = await cache.GetOrSetAsync(cacheKey, TimeSpan.FromHours(6), FetchAsync);
                return Results.Ok(result ?? new List<BrandFacetDto>());
            }

            return Results.Ok(await FetchAsync());
        });
    }
}
