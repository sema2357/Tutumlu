using System.Security.Claims;
using PriceAggregator.Api.Helpers;
using PriceAggregator.Core.Favorites;

namespace PriceAggregator.Api.Endpoints;

public static class FavoritesEndpoints
{
    public static void MapFavoritesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/favorites").RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal principal, FavoritesRepository favoritesRepository) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var rows = await favoritesRepository.GetByUserIdAsync(userId.Value);
            var items = rows.Select(mp =>
            {
                var unitPrice = mp.LowestPrice.HasValue
                    ? UnitPriceCalculator.Calculate(mp.LowestPrice.Value, mp.SizeValue, mp.SizeUnit, mp.PackQuantity)
                    : null;
                var discountPercent = mp.OldPrice.HasValue && mp.LowestPrice.HasValue && mp.OldPrice > mp.LowestPrice
                    ? Math.Round((mp.OldPrice.Value - mp.LowestPrice.Value) / mp.OldPrice.Value * 100, 0)
                    : (decimal?)null;
                return new
                {
                    mp.Id,
                    mp.CanonicalTitle,
                    mp.Brand,
                    mp.LowestPrice,
                    mp.OfferCount,
                    mp.ImageUrl,
                    UnitPrice = unitPrice?.Amount,
                    UnitLabel = unitPrice?.Label,
                    OldPrice = discountPercent.HasValue ? mp.OldPrice : null,
                    DiscountPercent = discountPercent
                };
            });

            return Results.Ok(items);
        });

        group.MapPost("/{productId:int}", async (int productId, ClaimsPrincipal principal, FavoritesRepository favoritesRepository) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var added = await favoritesRepository.AddAsync(userId.Value, productId);
            if (!added) return Results.NotFound(new { error = "Ürün bulunamadı." });

            return Results.Ok();
        });

        group.MapDelete("/{productId:int}", async (int productId, ClaimsPrincipal principal, FavoritesRepository favoritesRepository) =>
        {
            var userId = AuthHelpers.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            await favoritesRepository.RemoveAsync(userId.Value, productId);
            return Results.Ok();
        });
    }
}
