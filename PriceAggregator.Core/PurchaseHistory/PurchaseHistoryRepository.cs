using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.PurchaseHistory;

public class PurchaseHistoryRepository
{
    private readonly string _connectionString;

    public PurchaseHistoryRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<(IEnumerable<PurchaseHistoryRow> Items, decimal TotalMoneySaved)> GetByUserIdAsync(int userId)
    {
        using var db = new SqlConnection(_connectionString);

        var rows = await db.QueryAsync<PurchaseHistoryRow>(@"
            SELECT p.Id, ph.Id AS PurchaseId, p.Name AS CanonicalTitle, b.Name AS Brand,
                   ph.ActualPrice,
                   ph.DiscountedPrice,
                   (SELECT COUNT(1) FROM Offers o WHERE o.ProductId = p.Id) AS OfferCount,
                   p.ImageUrl,
                   p.SizeValue, p.SizeUnit, p.PackQuantity,
                   ph.PurchaseDate,
                   s.Name AS StoreName
            FROM PurchaseHistory ph
            JOIN Products p ON p.Id = ph.ProductId
            JOIN Brands b ON b.Id = p.BrandId
            JOIN Sellers s ON s.Id = ph.SellerId
            WHERE ph.UserId = @UserId
            ORDER BY ph.PurchaseDate DESC",
            new { UserId = userId });

        var totalMoneySaved = await db.ExecuteScalarAsync<decimal>(@"
            SELECT ISNULL(SUM(CASE WHEN ActualPrice > DiscountedPrice THEN ActualPrice - DiscountedPrice ELSE 0 END), 0)
            FROM PurchaseHistory
            WHERE UserId = @UserId",
            new { UserId = userId });

        return (rows, totalMoneySaved);
    }

    public async Task<bool> DeleteAsync(int userId, int purchaseId)
    {
        using var db = new SqlConnection(_connectionString);
        var affected = await db.ExecuteAsync(@"
            DELETE FROM PurchaseHistory
            WHERE (Id = @PurchaseId OR (ProductId = @PurchaseId AND UserId = @UserId)) AND UserId = @UserId",
            new { PurchaseId = purchaseId, UserId = userId });
        return affected > 0;
    }

    public async Task<bool> AddAsync(int userId, int productId, int sellerId, decimal actualPrice, decimal discountedPrice)
    {
        using var db = new SqlConnection(_connectionString);

        // Server-side validation: verify ProductId and SellerId exist
        var productExists = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Products WHERE Id = @ProductId", new { ProductId = productId });
        if (productExists == 0) return false;

        var sellerExists = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Sellers WHERE Id = @SellerId", new { SellerId = sellerId });
        if (sellerExists == 0) return false;

        // Teklifler arasındaki en yüksek fiyatı baz al:
        var maxOfferPrice = await db.ExecuteScalarAsync<decimal?>(
            "SELECT MAX(Price) FROM Offers WHERE ProductId = @ProductId", new { ProductId = productId });

        if (maxOfferPrice.HasValue && maxOfferPrice.Value > 0)
        {
            actualPrice = maxOfferPrice.Value;
        }
        else if (actualPrice <= 0)
        {
            actualPrice = discountedPrice;
        }

        await db.ExecuteAsync(@"
            INSERT INTO PurchaseHistory (UserId, ProductId, SellerId, ActualPrice, DiscountedPrice, PurchaseDate)
            VALUES (@UserId, @ProductId, @SellerId, @ActualPrice, @DiscountedPrice, GETUTCDATE())",
            new
            {
                UserId = userId,
                ProductId = productId,
                SellerId = sellerId,
                ActualPrice = actualPrice,
                DiscountedPrice = discountedPrice
            });

        return true;
    }
}
