using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Notifications;

public class NotificationRepository
{
    private readonly string _connectionString;

    public NotificationRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<NotificationRow>> GetByUserIdAsync(int userId)
    {
        using var db = new SqlConnection(_connectionString);
        return await db.QueryAsync<NotificationRow>(@"
            SELECT 
                n.NotificationId,
                n.UserId,
                n.OnSaleProductId,
                n.DiscountedPrice,
                n.NotificationMessage,
                n.NotificationDate,
                n.ReadStatus,
                p.Name AS CanonicalTitle,
                p.ImageUrl,
                MIN(o.Price) AS LowestPrice,
                MAX(o.RegularPrice) AS OldPrice,
                CASE 
                    WHEN MAX(o.RegularPrice) IS NOT NULL AND MAX(o.RegularPrice) > MIN(o.Price)
                    THEN ROUND((MAX(o.RegularPrice) - MIN(o.Price)) / MAX(o.RegularPrice) * 100, 0)
                    ELSE NULL
                END AS DiscountPercent
            FROM Notifications n
            JOIN Products p ON p.Id = n.OnSaleProductId
            LEFT JOIN Offers o ON o.ProductId = p.Id
            WHERE n.UserId = @UserId
            GROUP BY 
                n.NotificationId, n.UserId, n.OnSaleProductId, n.DiscountedPrice, 
                n.NotificationMessage, n.NotificationDate, n.ReadStatus, p.Name, p.ImageUrl
            ORDER BY n.NotificationDate DESC",
            new { UserId = userId });
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        using var db = new SqlConnection(_connectionString);
        return await db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) 
            FROM Notifications 
            WHERE UserId = @UserId AND ReadStatus = 0",
            new { UserId = userId });
    }

    public async Task MarkAsReadAsync(int userId, int notificationId)
    {
        using var db = new SqlConnection(_connectionString);
        await db.ExecuteAsync(@"
            UPDATE Notifications 
            SET ReadStatus = 1 
            WHERE UserId = @UserId AND NotificationId = @NotificationId",
            new { UserId = userId, NotificationId = notificationId });

        await CleanupOldReadNotificationsAsync();
    }

    public async Task MarkAllAsReadAsync(int userId)
    {
        using var db = new SqlConnection(_connectionString);
        await db.ExecuteAsync(@"
            UPDATE Notifications
            SET ReadStatus = 1
            WHERE UserId = @UserId AND ReadStatus = 0",
            new { UserId = userId });

        await CleanupOldReadNotificationsAsync();
    }

    public async Task DeleteAsync(int userId, int notificationId)
    {
        using var db = new SqlConnection(_connectionString);
        await db.ExecuteAsync(
            "DELETE FROM Notifications WHERE UserId = @UserId AND NotificationId = @NotificationId",
            new { UserId = userId, NotificationId = notificationId });
    }

    public async Task CleanupOldReadNotificationsAsync()
    {
        using var db = new SqlConnection(_connectionString);
        await db.ExecuteAsync(@"
            WITH ReadNotificationsRanked AS (
                SELECT 
                    NotificationId,
                    ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY NotificationDate DESC) AS RowNum
                FROM Notifications
                WHERE ReadStatus = 1
            )
            DELETE FROM Notifications
            WHERE NotificationId IN (
                SELECT NotificationId 
                FROM ReadNotificationsRanked 
                WHERE RowNum > 5
            )");
    }

    public async Task SyncFavoriteNotificationsForUserAsync(int userId)
    {
        using var db = new SqlConnection(_connectionString);

        // Find all favorited items for this user that currently have a valid discount (RegularPrice > Price)
        var discountedFavorites = await db.QueryAsync<(int ProductId, string Title, decimal LowestPrice, decimal OldPrice, decimal DiscountPercent)>(@"
            SELECT 
                p.Id AS ProductId,
                p.Name AS Title,
                MIN(o.Price) AS LowestPrice,
                MAX(o.RegularPrice) AS OldPrice,
                ROUND((MAX(o.RegularPrice) - MIN(o.Price)) / MAX(o.RegularPrice) * 100, 0) AS DiscountPercent
            FROM Favorites f
            JOIN Products p ON p.Id = f.ProductId
            JOIN Offers o ON o.ProductId = p.Id
            WHERE f.UserId = @UserId 
              AND o.RegularPrice IS NOT NULL 
              AND o.RegularPrice > o.Price
            GROUP BY p.Id, p.Name
            HAVING MAX(o.RegularPrice) > MIN(o.Price)",
            new { UserId = userId });

        foreach (var item in discountedFavorites)
        {
            // Insert notification ONLY IF no notification for this item has been sent to this user in the last 7 days
            var message = $"Favorilerinizdeki '{item.Title}' ürünü %{item.DiscountPercent:0} indirimle ₺{item.LowestPrice:N2} fiyatına düştü!";

            await db.ExecuteAsync(@"
                IF NOT EXISTS (
                    SELECT 1 FROM Notifications 
                    WHERE UserId = @UserId 
                      AND OnSaleProductId = @ProductId 
                      AND NotificationDate >= DATEADD(day, -7, GETUTCDATE())
                )
                BEGIN
                    INSERT INTO Notifications (UserId, OnSaleProductId, DiscountedPrice, NotificationMessage, NotificationDate, ReadStatus)
                    VALUES (@UserId, @ProductId, @LowestPrice, @Message, GETUTCDATE(), 0)
                END",
                new { UserId = userId, ProductId = item.ProductId, LowestPrice = item.LowestPrice, Message = message });
        }
    }
}
