namespace PriceAggregator.Core.Pricing;

// Turns a price plus the size SizeParser already extracted from the title (SizeValue/
// SizeUnit/PackQuantity, stored once per MasterProduct at match time) into a per-Kg/
// per-Litre/per-Adet figure — the "birim fiyat" shoppers use to compare, say, a 1.5L
// bottle against a 6x330ml pack regardless of each product's own package size.
public static class UnitPriceCalculator
{
    public static (decimal Amount, string Label)? Calculate(
        decimal price, decimal? sizeValue, string? sizeUnit, int packQuantity)
    {
        if (sizeValue is null || sizeValue.Value <= 0 || string.IsNullOrWhiteSpace(sizeUnit))
            return null;

        var totalSize = sizeValue.Value * packQuantity;
        if (totalSize <= 0) return null;

        return sizeUnit switch
        {
            "G" => (price / (totalSize / 1000m), "Kg"),
            "ML" => (price / (totalSize / 1000m), "Litre"),
            "ADET" => (price / totalSize, "Adet"),
            _ => null
        };
    }
}
