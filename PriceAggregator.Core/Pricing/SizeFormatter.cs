namespace PriceAggregator.Core.Pricing;

// Turns the SizeParser-derived (SizeValue, SizeUnit, PackQuantity) — already sitting on every
// Products row for UnitPriceCalculator's birim fiyat — into a human filter label: "Hacim" for
// liquids (Litre/Ml), "Ağırlık" for solids (Kg/G). This is the same data already captured at
// match time, just displayed instead of turned into a price, so a shopper can filter "İçecek"
// by litre or a "Kg" grocery item by weight with no new scraping.
//
// Hacim/Ağırlık are bucketed into a handful of ranges rather than the exact size — a catalog
// this size has a near-unique ml/gram value per product (500 Ml, 750 Ml, 1 L, 1,5 L, 2 L, 2,5
// L, ...), which turned the checkbox list into as much noise as the unsplit color combos were.
// ADET (pack count) isn't turned into a filter at all — a raw piece count isn't a meaningful
// shopping facet the way volume/weight are, so it's dropped rather than bucketed.
public static class SizeFormatter
{
    public static (string AttributeName, string Value)? Format(decimal? sizeValue, string? sizeUnit, int packQuantity)
    {
        if (sizeValue is null || sizeValue.Value <= 0 || string.IsNullOrWhiteSpace(sizeUnit))
            return null;

        var total = sizeValue.Value * packQuantity;

        return sizeUnit switch
        {
            "G" => ("Ağırlık", WeightBucket(total)),
            "ML" => ("Hacim", VolumeBucket(total)),
            _ => null
        };
    }

    public static IEnumerable<(string AttributeName, string Value)> FormatAttributes(decimal? sizeValue, string? sizeUnit, int packQuantity)
    {
        var primary = Format(sizeValue, sizeUnit, packQuantity);
        if (primary is not null)
            yield return primary.Value;

        int totalPieces = (sizeUnit == "ADET" && sizeValue is not null && sizeValue > 0)
            ? (int)(sizeValue.Value * packQuantity)
            : Math.Max(1, packQuantity);

        yield return ("Miktar", QuantityBucket(totalPieces));
    }

    public static string QuantityBucket(int totalPieces) => totalPieces switch
    {
        1 => "1 Adet (Tekli)",
        2 => "2 Adet",
        3 => "3 Adet",
        4 => "4 Adet",
        5 or 6 => "5 - 6 Adet",
        >= 7 and <= 12 => "7 - 12 Adet",
        >= 13 and <= 24 => "13 - 24 Adet",
        >= 25 and <= 50 => "25 - 50 Adet",
        _ => "50 Adet ve üzeri"
    };

    // Bucket ranks (VolumeBucketRank/WeightBucketRank below) keep these in small-to-large
    // order wherever they're listed — plain alphabetical sort would put "500 Ml ve altı" after
    // "5 Litre ve üzeri" since string comparison doesn't know "500" > "5".
    public static string VolumeBucket(decimal totalMl) => totalMl switch
    {
        <= 500 => "500 Ml ve altı",
        <= 1000 => "500 Ml - 1 Litre",
        <= 2000 => "1 - 2 Litre",
        <= 5000 => "2 - 5 Litre",
        _ => "5 Litre ve üzeri"
    };

    public static string WeightBucket(decimal totalG) => totalG switch
    {
        <= 250 => "250 G ve altı",
        <= 500 => "250 - 500 G",
        <= 1000 => "500 G - 1 Kg",
        <= 2000 => "1 - 2 Kg",
        _ => "2 Kg ve üzeri"
    };
}
