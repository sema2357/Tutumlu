using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Data;

public class ProductRepository
{
    private readonly string _connectionString;

    public ProductRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int?> SaveAsync(string source, ProductDto product)
    {
        var saved = await SaveBatchAsync(source, new[] { product });
        return saved.Count > 0 ? saved[0].SellerProductId : null;
    }

    public async Task<List<(int SellerProductId, ProductDto Product)>> SaveBatchAsync(string source, IReadOnlyList<ProductDto> products)
    {
        var result = new List<(int SellerProductId, ProductDto Product)>();
        if (products == null || products.Count == 0) return result;

        var distinctProducts = products.Where(p => p != null && p.ExternalId != 0)
                                       .DistinctBy(p => p.ExternalId)
                                       .ToList();

        try
        {
            using var db = new SqlConnection(_connectionString);
            await db.OpenAsync();

            var sellerId = await FindOrCreateSellerAsync(db, source);

            const string sql = @"
                MERGE SellerProducts AS target
                USING (SELECT @SellerId AS SellerId, @SellerProductCode AS SellerProductCode) AS src
                ON target.SellerId = src.SellerId AND target.SellerProductCode = src.SellerProductCode
                WHEN MATCHED THEN
                    UPDATE SET ExternalTitle=@ExternalTitle, ExternalUrl=@ExternalUrl, ExternalImageUrl=@ExternalImageUrl,
                               CurrentPrice=@CurrentPrice, RegularPrice=@RegularPrice, UnitType=@UnitType, UnitAmount=@UnitAmount,
                               SourceUnitPrice=@SourceUnitPrice, LastScrapedAt=GETDATE(), StoreName=@StoreName,
                               OriginalCategory=COALESCE(@OriginalCategory, target.OriginalCategory)
                WHEN NOT MATCHED THEN
                    INSERT (SellerId, SellerProductCode, ExternalTitle, ExternalUrl, ExternalImageUrl,
                            CurrentPrice, RegularPrice, UnitType, UnitAmount, SourceUnitPrice, StoreName, OriginalCategory, LastScrapedAt)
                    VALUES (@SellerId, @SellerProductCode, @ExternalTitle, @ExternalUrl, @ExternalImageUrl,
                            @CurrentPrice, @RegularPrice, @UnitType, @UnitAmount, @SourceUnitPrice, @StoreName, @OriginalCategory, GETDATE())
                OUTPUT INSERTED.Id;";

            using var transaction = db.BeginTransaction();
            foreach (var product in distinctProducts)
            {
                try
                {
                    var origCategory = (product.CategoryPath != null && product.CategoryPath.Count > 0)
                        ? string.Join(" > ", product.CategoryPath)
                        : product.CategoryName;

                    var id = await db.QuerySingleAsync<int>(sql, new
                    {
                        SellerId = sellerId,
                        SellerProductCode = product.ExternalId.ToString(),
                        ExternalTitle = product.Title,
                        ExternalUrl = product.ProductUrl,
                        ExternalImageUrl = product.ImageUrl,
                        CurrentPrice = product.Price,
                        RegularPrice = product.RegularPrice > product.Price ? product.RegularPrice : (decimal?)null,
                        UnitType = product.SourceUnitType,
                        UnitAmount = product.SourceUnitAmount,
                        SourceUnitPrice = product.SourceUnitPrice,
                        StoreName = product.StoreName,
                        OriginalCategory = origCategory
                    }, transaction: transaction);

                    result.Add((id, product));
                }
                catch (Exception ex)
                {
                    Logger.Log($"[DB] Failed to save product '{product.Title}' ({source}): {ex.Message}");
                }
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            Logger.Log($"[DB] Batch save error for '{source}': {ex.Message}");
        }

        return result;
    }

    private static async Task<int> FindOrCreateSellerAsync(SqlConnection db, string name)
    {
        var existing = await db.QuerySingleOrDefaultAsync<int?>(
            "SELECT Id FROM Sellers WHERE Name = @Name", new { Name = name });
        if (existing.HasValue) return existing.Value;

        try
        {
            return await db.QuerySingleAsync<int>(
                "INSERT INTO Sellers (Name) OUTPUT INSERTED.Id VALUES (@Name)", new { Name = name });
        }
        catch (SqlException)
        {
            return await db.QuerySingleAsync<int>(
                "SELECT Id FROM Sellers WHERE Name = @Name", new { Name = name });
        }
    }
}
