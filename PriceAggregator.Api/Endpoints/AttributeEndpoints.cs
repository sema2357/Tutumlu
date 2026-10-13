using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;
using PriceAggregator.Core.Pricing;

namespace PriceAggregator.Api.Endpoints;

public static class AttributeEndpoints
{
    public static void MapAttributeEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/attributes", async (
            string? category, 
            string? q, 
            string[]? brand, 
            string[]? store, 
            string[]? attr, 
            decimal? minPrice, 
            decimal? maxPrice, 
            bool? onSale, 
            ICacheService cache) =>
        {
            async Task<List<AttributeGroupRow>> FetchAsync()
            {
                // Bütün ürünlerin listelendiği yerde varyant filtrelerini getirme
                if (string.IsNullOrWhiteSpace(category))
                {
                    return new List<AttributeGroupRow>();
                }

                using var db = new SqlConnection(connectionString);

                // Ana sektör kategorisinde de (ParentCategoryId IS NULL) varyant filtrelerini getirme
                var isRootSector = await db.ExecuteScalarAsync<bool>(@"
                    SELECT CASE WHEN EXISTS (
                        SELECT 1 FROM Categories WHERE Slug = @CategorySlug AND ParentCategoryId IS NULL
                    ) THEN 1 ELSE 0 END", new { CategorySlug = category });
                if (isRootSector)
                {
                    return new List<AttributeGroupRow>();
                }

                var selectedGroups = ProductFilterHelpers.ParseAttrGroups(attr);

                async Task<IEnumerable<(string AttributeName, string AttributeValue, int ProductCount)>> QueryGroupAsync(string? onlyGroupName)
                {
                    var (whereClause, parameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
                    ProductFilterHelpers.AppendBrandFilter(brand, ref whereClause, parameters);
                    ProductFilterHelpers.AppendStoreFilter(store, ref whereClause, parameters);

                    var otherGroups = onlyGroupName is null
                        ? selectedGroups
                        : selectedGroups.Where(g => g.Name != onlyGroupName).ToList();
                    ProductFilterHelpers.AppendAttrGroupFilters(otherGroups, ref whereClause, parameters);

                    whereClause += " AND pa.AttributeName NOT IN ('Adet', 'Renk')";
                    if (onlyGroupName is not null)
                    {
                        whereClause += " AND pa.AttributeName = @OnlyGroupName";
                        parameters.Add("OnlyGroupName", onlyGroupName);
                    }
                    else if (selectedGroups.Count > 0)
                    {
                        whereClause += " AND pa.AttributeName NOT IN @SelectedGroupNames";
                        parameters.Add("SelectedGroupNames", selectedGroups.Select(g => g.Name).ToList());
                    }

                    var sql = $@"
                        SELECT pa.AttributeName, pa.AttributeValue, COUNT(DISTINCT pa.ProductId) AS ProductCount
                        FROM ProductAttributes pa
                        JOIN Products p ON p.Id = pa.ProductId
                        {whereClause}
                        GROUP BY pa.AttributeName, pa.AttributeValue";

                    return await db.QueryAsync<(string AttributeName, string AttributeValue, int ProductCount)>(sql, parameters);
                }

                var rows = (await QueryGroupAsync(null)).ToList();
                foreach (var group in selectedGroups)
                    rows.AddRange(await QueryGroupAsync(group.Name));

                var (totalWhereClause, totalParameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(db, category, q, minPrice, maxPrice, onSale);
                ProductFilterHelpers.AppendBrandFilter(brand, ref totalWhereClause, totalParameters);
                ProductFilterHelpers.AppendStoreFilter(store, ref totalWhereClause, totalParameters);
                var totalProductCount = await db.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(DISTINCT p.Id) FROM Products p {totalWhereClause}", totalParameters);

                if (totalProductCount > 50)
                {
                    var selectedKeys = selectedGroups
                        .SelectMany(g => g.Values.Select(v => (Name: g.Name, Value: v)))
                        .ToHashSet();
                    var minCount = Math.Max(3, (int)Math.Ceiling(totalProductCount * 0.01));

                    rows = rows
                        .Where(r => r.ProductCount >= minCount || selectedKeys.Contains((r.AttributeName, r.AttributeValue)))
                        .ToList();
                }

                static int GetGroupPriority(string name) => name switch
                {
                    "Hacim" => 1,
                    "Miktar" => 2,
                    "Ağırlık" => 3,
                    "Aroma" => 4,
                    "Diyet / Özellik" => 5,
                    _ => 10
                };

                return rows
                    .GroupBy(r => r.AttributeName)
                    .Select(g => new AttributeGroupRow(
                        g.Key,
                        g.Select(r => new { Row = r, Key = SizeParser.GetMagnitudeKey(r.AttributeValue) })
                         .OrderBy(x => x.Key.HasNumeric ? 0 : 1)
                         .ThenBy(x => x.Key.MinValue)
                         .ThenBy(x => x.Key.MaxValue)
                         .ThenBy(x => x.Row.AttributeValue)
                         .Select(x => new AttributeValueRow(x.Row.AttributeValue, x.Row.ProductCount))
                         .ToList()
                    ))
                    .OrderBy(g => GetGroupPriority(g.Name))
                    .ThenBy(g => g.Name)
                    .ToList();
            }

            var hasNarrowingFilters = !string.IsNullOrWhiteSpace(q) || (brand?.Length > 0) || (store?.Length > 0) || (attr?.Length > 0) || minPrice.HasValue || maxPrice.HasValue || onSale == true;
            if (!hasNarrowingFilters)
            {
                var cacheKey = $"attributes_{category ?? "all"}";
                var result = await cache.GetOrSetAsync(cacheKey, TimeSpan.FromHours(6), FetchAsync);
                return Results.Ok(result ?? new List<AttributeGroupRow>());
            }

            return Results.Ok(await FetchAsync());
        });
    }
}

public record AttributeGroupRow(string Name, List<AttributeValueRow> Values);
public record AttributeValueRow(string AttributeValue, int ProductCount);
