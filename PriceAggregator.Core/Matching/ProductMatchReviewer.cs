using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Matching;

public static class ProductMatchReviewer
{
    public record SuspectMatch(
        int ProductId, string CanonicalTitle, int OfferId, int SellerId, string SellerName,
        int SellerProductId, string ExternalTitle, double Score,
        int BrandId = 0, string? BrandName = null, decimal? SizeValue = null, string? SizeUnit = null,
        int PackQuantity = 1, int? CategoryId = null, string? ExternalImageUrl = null);

    public record FixOutcome(
        int OfferId, string SellerName, string ExternalTitle, int OldProductId, int? NewProductId, string Action);

    public static async Task<List<SuspectMatch>> FindSuspectsAsync(SqlConnection db, IEnumerable<int>? offerIds = null)
    {
        await TitleSimilarity.EnsureModifiersLoadedAsync(db);
        var idList = offerIds?.ToList();
        var rows = await db.QueryAsync<(int ProductId, string CanonicalTitle, int BrandId, string? BrandName, int OfferId,
            int SellerId, string SellerName, int SellerProductId, string ExternalTitle, string? ExternalImageUrl,
            decimal? SizeValue, string? SizeUnit, int PackQuantity, int? CategoryId)>($@"
            SELECT p.Id AS ProductId, p.Name AS CanonicalTitle, p.BrandId, b.Name AS BrandName,
                   o.Id AS OfferId, o.SellerId, s.Name AS SellerName,
                   sp.Id AS SellerProductId, sp.ExternalTitle, sp.ExternalImageUrl,
                   p.SizeValue, p.SizeUnit, p.PackQuantity, p.CategoryId
            FROM Offers o
            JOIN Products p ON p.Id = o.ProductId
            JOIN SellerProducts sp ON sp.Id = o.SellerProductId
            JOIN Sellers s ON s.Id = o.SellerId
            LEFT JOIN Brands b ON b.Id = p.BrandId
            WHERE o.IsActive = 1
            {(idList is { Count: > 0 } ? "AND o.Id IN @OfferIds" : "")}",
            new { OfferIds = idList });

        var suspects = new List<SuspectMatch>();
        foreach (var row in rows)
        {
            var score = TitleSimilarity.Calculate(row.CanonicalTitle, row.ExternalTitle, row.BrandName, row.SizeValue, row.PackQuantity);
            if (score < MatchingService.MatchThreshold)
            {
                suspects.Add(new SuspectMatch(
                    row.ProductId, row.CanonicalTitle, row.OfferId, row.SellerId, row.SellerName,
                    row.SellerProductId, row.ExternalTitle, score,
                    row.BrandId, row.BrandName, row.SizeValue, row.SizeUnit,
                    row.PackQuantity, row.CategoryId, row.ExternalImageUrl));
            }
        }

        return suspects;
    }

    public static async Task<List<FixOutcome>> FixSuspectsAsync(SqlConnection db, IEnumerable<int>? offerIds = null)
    {
        var suspects = await FindSuspectsAsync(db, offerIds);
        var outcomes = new List<FixOutcome>();

        foreach (var suspect in suspects)
        {
            var brandName = suspect.BrandName ?? "Unknown";

            var (matchId, score) = await MatchingService.FindBestMatchAsync(
                db, suspect.BrandId, suspect.SizeValue, suspect.SizeUnit, suspect.PackQuantity,
                suspect.ExternalTitle, brandName, excludeProductId: suspect.ProductId);

            int targetProductId;
            string action;

            if (matchId.HasValue && score >= MatchingService.MatchThreshold)
            {
                var conflict = await db.QuerySingleOrDefaultAsync<int?>(
                    "SELECT Id FROM Offers WHERE ProductId = @ProductId AND SellerId = @SellerId AND Id <> @OfferId",
                    new { ProductId = matchId.Value, suspect.SellerId, suspect.OfferId });

                if (conflict.HasValue)
                {
                    outcomes.Add(new FixOutcome(
                        suspect.OfferId, suspect.SellerName, suspect.ExternalTitle, suspect.ProductId, null,
                        "Skipped: seller already has an offer on the target product (needs manual review)"));
                    continue;
                }

                targetProductId = matchId.Value;
                action = "Moved to existing product";
            }
            else
            {
                var variantKey = VariantParser.ExtractVariant(suspect.ExternalTitle);
                targetProductId = await db.QuerySingleAsync<int>(@"
                    INSERT INTO Products (Name, BrandId, SizeValue, SizeUnit, PackQuantity, CategoryId, ImageUrl, VariantKey)
                    OUTPUT INSERTED.Id
                    VALUES (@Title, @BrandId, @SizeValue, @SizeUnit, @PackQuantity, @CategoryId, @ImageUrl, @VariantKey)",
                    new
                    {
                        Title = suspect.ExternalTitle,
                        BrandId = suspect.BrandId,
                        SizeValue = suspect.SizeValue,
                        SizeUnit = suspect.SizeUnit,
                        PackQuantity = suspect.PackQuantity,
                        CategoryId = suspect.CategoryId,
                        ImageUrl = suspect.ExternalImageUrl,
                        VariantKey = variantKey
                    });
                action = "Created new product";
            }

            await db.ExecuteAsync(
                "UPDATE Offers SET ProductId = @ProductId WHERE Id = @OfferId",
                new { ProductId = targetProductId, suspect.OfferId });

            await db.ExecuteAsync(
                @"INSERT INTO ProductMatchingLogs (SellerProductId, ProductId, Status, MatchedBy, Notes)
                  VALUES (@SellerProductId, @ProductId, 'Rematched', 'ProductMatchReviewer', @Notes)",
                new
                {
                    suspect.SellerProductId,
                    ProductId = targetProductId,
                    Notes = $"movedFrom={suspect.ProductId}; score={score:F2}"
                });

            await db.ExecuteAsync(@"
                IF NOT EXISTS (SELECT 1 FROM Offers WHERE ProductId = @OldProductId)
                BEGIN
                    DELETE FROM ProductMatchingLogs WHERE ProductId = @OldProductId;
                    DELETE FROM ProductAttributes WHERE ProductId = @OldProductId;
                    DELETE FROM ProductSpecs WHERE ProductId = @OldProductId;
                    DELETE FROM Products WHERE Id = @OldProductId;
                END",
                new { OldProductId = suspect.ProductId });

            outcomes.Add(new FixOutcome(
                suspect.OfferId, suspect.SellerName, suspect.ExternalTitle, suspect.ProductId, targetProductId, action));
        }

        return outcomes;
    }
}
