namespace PriceAggregator.Core.Notifications;

public record NotificationRow(
    int NotificationId,
    int UserId,
    int OnSaleProductId,
    decimal DiscountedPrice,
    string NotificationMessage,
    DateTime NotificationDate,
    bool ReadStatus,
    string CanonicalTitle,
    string? ImageUrl,
    decimal LowestPrice,
    decimal? OldPrice,
    decimal? DiscountPercent
);
