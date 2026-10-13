using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Api.Services;

namespace PriceAggregator.Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/categories", async (bool? refresh, ICacheService cache) =>
        {
            if (refresh == true)
            {
                await cache.RemoveAsync("categories_active_v6");
            }

            var categories = await cache.GetOrSetAsync<List<CategoryCacheRow>>("categories_active_v6", TimeSpan.FromMinutes(10), async () =>
            {
                using var db = new SqlConnection(connectionString);
                var rows = (await db.QueryAsync<CategoryCacheRow>(@"
                
                
                    SELECT c.Id, c.Name, c.Slug, c.ParentCategoryId, c.IconClass, ISNULL(pc.Count, 0) AS ProductCount
                    FROM Categories c
                    LEFT JOIN (SELECT CategoryId, COUNT(1) AS Count FROM Products GROUP BY CategoryId) pc ON pc.CategoryId = c.Id
                    WHERE c.IsActive = 1")).ToList();

                var dict = rows.ToDictionary(r => r.Id);
                var directCounts = rows.ToDictionary(r => r.Id, r => r.ProductCount);

                // Roll up direct product counts up the category tree to all ancestors
                foreach (var (categoryId, count) in directCounts)
                {
                    if (count <= 0) continue;

                    var currentParentId = dict[categoryId].ParentCategoryId;
                    var visited = new HashSet<int>();
                    while (currentParentId.HasValue && visited.Add(currentParentId.Value))
                    {
                        if (dict.TryGetValue(currentParentId.Value, out var parent))
                        {
                            parent.ProductCount += count;
                            currentParentId = parent.ParentCategoryId;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                return rows
                    .OrderBy(r => r.ParentCategoryId ?? 0)
                    .ThenBy(r => r.Name)
                    .ToList();
            });
            return Results.Ok(categories);
        });

        app.MapGet("/api/categories/{slug}/breadcrumb", async (string slug) =>
        {
            using var db = new SqlConnection(connectionString);
            var breadcrumb = await db.QueryAsync<CategoryCacheRow>(@"
                WITH CatTree AS (
                    SELECT Id, Name, Slug, ParentCategoryId, IconClass, 0 AS Lvl
                    FROM Categories WHERE Slug = @Slug
                    UNION ALL
                    SELECT c.Id, c.Name, c.Slug, c.ParentCategoryId, c.IconClass, ct.Lvl + 1
                    FROM Categories c
                    JOIN CatTree ct ON c.Id = ct.ParentCategoryId
                )
                SELECT Id, Name, Slug, ParentCategoryId, IconClass, 0 AS ProductCount
                FROM CatTree
                ORDER BY Lvl DESC", new { Slug = slug });

            return Results.Ok(breadcrumb);
        });

        app.MapGet("/api/search-categories", async (string? q) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.Ok(Array.Empty<object>());

            using var db = new SqlConnection(connectionString);

            var (whereClause, parameters, _) = await ProductFilterHelpers.BuildBaseProductFilterAsync(
                db, category: null, q, minPrice: null, maxPrice: null, onSale: null);

            var sql = $@"
                SELECT p.CategoryId, COUNT(DISTINCT p.Id) AS ProductCount
                FROM Products p
                {whereClause}
                AND p.CategoryId IS NOT NULL
                GROUP BY p.CategoryId";

            var rows = await db.QueryAsync(sql, parameters, commandTimeout: 15);
            return Results.Ok(rows);
        });

        app.MapPost("/api/categories/align-trees", async (ICacheService cache) =>
        {
            using var db = new SqlConnection(connectionString);
            await db.OpenAsync();
            int mapped = await PriceAggregator.Core.Categorization.CategoryResolver.AlignUnmappedShopCategoriesAsync(db);
            await cache.RemoveAsync("categories_active_v5");
            return Results.Ok(new { MappedCount = mapped, Message = $"{mapped} market categories aligned and products updated." });
        });
    }
}

public class CategoryCacheRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public int? ParentCategoryId { get; set; }
    public string IconClass { get; set; } = "fa fa-cube";
    public string? IconSlug => IconClass;
    public int ProductCount { get; set; }
}
