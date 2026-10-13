using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Favorites;

public class FavoritesRepository
{
    private readonly string _connectionString;

    public FavoritesRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<FavoriteProductRow>> GetByUserIdAsync(int userId)
    {
        using var db = new SqlConnection(_connectionString);
        return await db.QueryAsync<FavoriteProductRow>(@"
            SELECT p.Id, p.Name AS CanonicalTitle, b.Name AS Brand,
                   MIN(o.Price) AS LowestPrice,
                   COUNT(o.Id) AS OfferCount,
                   p.ImageUrl,
                   p.SizeValue, p.SizeUnit, p.PackQuantity,
                   MAX(o.RegularPrice) AS OldPrice
            FROM Favorites f
            JOIN Products p ON p.Id = f.ProductId
            JOIN Brands b ON b.Id = p.BrandId
            LEFT JOIN Offers o ON o.ProductId = p.Id
            WHERE f.UserId = @UserId
            GROUP BY p.Id, p.Name, b.Name, p.ImageUrl, p.SizeValue, p.SizeUnit, p.PackQuantity, f.CreatedAt
            ORDER BY f.CreatedAt DESC",
            new { UserId = userId });
    }

    public async Task<bool> AddAsync(int userId, int productId)
    {
        using var db = new SqlConnection(_connectionString);
        try
        {
            await db.ExecuteAsync(
                "INSERT INTO Favorites (UserId, ProductId) VALUES (@UserId, @ProductId)",
                new { UserId = userId, ProductId = productId });
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return true;
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            return false;
        }
    }

    public async Task RemoveAsync(int userId, int productId)
    {
        using var db = new SqlConnection(_connectionString);
        await db.ExecuteAsync(
            "DELETE FROM Favorites WHERE UserId = @UserId AND ProductId = @ProductId",
            new { UserId = userId, ProductId = productId });
    }
}
