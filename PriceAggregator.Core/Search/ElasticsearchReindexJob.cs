using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Search;

public class ElasticsearchReindexJob
{
    private readonly string _connectionString;
    private readonly ElasticsearchService _elasticsearchService;

    public ElasticsearchReindexJob(string connectionString, ElasticsearchService elasticsearchService)
    {
        _connectionString = connectionString;
        _elasticsearchService = elasticsearchService;
    }

    public async Task<(int TotalProducts, int IndexedProducts)> RunAsync()
    {
        using var db = new SqlConnection(_connectionString);

        var sql = @"
            SELECT 
                p.Id,
                p.Name,
                p.BrandId,
                b.Name AS BrandName,
                p.CategoryId,
                c.Name AS CategoryName,
                c.SectorId,
                p.ImageUrl,
                MIN(o.Price) AS LowestPrice,
                MIN(o.RegularPrice) AS RegularPrice,
                COUNT(o.Id) AS OfferCount
            FROM Products p
            JOIN Brands b ON b.Id = p.BrandId
            LEFT JOIN Categories c ON c.Id = p.CategoryId
            JOIN Offers o ON o.ProductId = p.Id AND o.IsActive = 1
            GROUP BY p.Id, p.Name, p.BrandId, b.Name, p.CategoryId, c.Name, c.SectorId, p.ImageUrl";

        var rows = (await db.QueryAsync<dynamic>(sql)).ToList();

        var indexModels = new List<ProductIndexModel>();
        foreach (var row in rows)
        {
            decimal lowest = row.LowestPrice != null ? (decimal)row.LowestPrice : 0m;
            decimal? regular = row.RegularPrice != null ? (decimal?)row.RegularPrice : null;
            bool isOnSale = regular.HasValue && regular.Value > lowest;

            indexModels.Add(new ProductIndexModel
            {
                Id = (int)row.Id,
                Name = (string)row.Name,
                BrandId = (int)row.BrandId,
                BrandName = (string)row.BrandName,
                CategoryId = row.CategoryId != null ? (int?)row.CategoryId : null,
                CategoryName = row.CategoryName != null ? (string)row.CategoryName : null,
                SectorId = row.SectorId != null ? (int?)row.SectorId : null,
                ImageUrl = row.ImageUrl != null ? (string)row.ImageUrl : null,
                LowestPrice = lowest,
                RegularPrice = regular,
                IsOnSale = isOnSale,
                OfferCount = (int)row.OfferCount
            });
        }

        if (indexModels.Count == 0)
        {
            return (0, 0);
        }

        int indexed = await _elasticsearchService.BulkIndexProductsAsync(indexModels, recreateIndex: true);
        return (indexModels.Count, indexed);
    }
}
