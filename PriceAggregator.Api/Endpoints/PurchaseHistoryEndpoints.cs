using System.Security.Claims;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Core.PurchaseHistory;

namespace PriceAggregator.Api.Endpoints;

public static class PurchaseHistoryEndpoints
{
    public static void MapPurchaseHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/purchase-history").RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal principal, PurchaseHistoryRepository repository) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var (rows, totalMoneySaved) = await repository.GetByUserIdAsync(userId.Value);
            var items = rows.Select(mp =>
            {
                var unitPrice = UnitPriceCalculator.Calculate(mp.DiscountedPrice, mp.SizeValue, mp.SizeUnit, mp.PackQuantity);
                var discountPercent = mp.ActualPrice > mp.DiscountedPrice
                    ? Math.Round((mp.ActualPrice - mp.DiscountedPrice) / mp.ActualPrice * 100, 0)
                    : (decimal?)null;
                return new
                {
                    mp.Id,
                    PurchaseId = mp.PurchaseId,
                    mp.CanonicalTitle,
                    mp.Brand,
                    LowestPrice = mp.DiscountedPrice,
                    OfferCount = mp.OfferCount,
                    mp.ImageUrl,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label,
                    OldPrice = discountPercent.HasValue ? mp.ActualPrice : (decimal?)null,
                    DiscountPercent = discountPercent,
                    mp.PurchaseDate,
                    LowestPriceStore = mp.StoreName
                };
            });

            return Results.Ok(new
            {
                items,
                totalMoneySaved
            });
        });

        group.MapDelete("/{id}", async (int id, ClaimsPrincipal principal, PurchaseHistoryRepository repository) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var deleted = await repository.DeleteAsync(userId.Value, id);
            if (!deleted) return Results.NotFound(new { error = "Satın alma kaydı bulunamadı." });

            var (_, totalMoneySaved) = await repository.GetByUserIdAsync(userId.Value);
            return Results.Ok(new { message = "Satın alma kaydı başarıyla kaldırıldı.", totalMoneySaved });
        });

        group.MapPost("/confirm", async (ConfirmPurchaseRequest req, ClaimsPrincipal principal, PurchaseHistoryRepository repository) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            if (req.ProductId <= 0 || req.SellerId <= 0)
            {
                return Results.BadRequest(new { error = "Geçersiz ürün veya satıcı bilgisi." });
            }

            var confirmed = await repository.AddAsync(userId.Value, req.ProductId, req.SellerId, req.ActualPrice, req.DiscountedPrice);
            if (!confirmed)
            {
                return Results.NotFound(new { error = "Ürün veya satıcı sistemde bulunamadı." });
            }

            return Results.Ok(new { message = "Satın alma kaydı başarıyla oluşturuldu." });
        });
    }
}

public record ConfirmPurchaseRequest(int ProductId, int SellerId, decimal ActualPrice, decimal DiscountedPrice);
