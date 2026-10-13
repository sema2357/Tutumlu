namespace PriceAggregator.Core.Favorites;

public record FavoriteProductRow(int Id, string CanonicalTitle, string? Brand, decimal? LowestPrice,
    int OfferCount, string? ImageUrl, decimal? SizeValue, string? SizeUnit, int PackQuantity,
    decimal? OldPrice);
