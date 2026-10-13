namespace PriceAggregator.Core.PurchaseHistory;

public class PurchaseHistoryRow
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public string CanonicalTitle { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal ActualPrice { get; set; }
    public decimal DiscountedPrice { get; set; }
    public int OfferCount { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? SizeValue { get; set; }
    public string? SizeUnit { get; set; }
    public int PackQuantity { get; set; } = 1;
    public DateTime PurchaseDate { get; set; }
    public string? StoreName { get; set; }
}
