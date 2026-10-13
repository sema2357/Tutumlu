using System.Globalization;
using System.Text.RegularExpressions;
using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Pricing;

public static class SizeParser
{
    private static readonly Dictionary<string, (string CanonicalUnit, decimal Multiplier)> UnitMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gr"] = ("G", 1),
            ["g"] = ("G", 1),
            ["gram"] = ("G", 1),
            ["kg"] = ("G", 1000),
            ["kilo"] = ("G", 1000),
            ["kilogram"] = ("G", 1000),
            ["mg"] = ("G", 0.001m),
            ["miligram"] = ("G", 0.001m),
            ["ton"] = ("G", 1000000m),

            ["ml"] = ("ML", 1),
            ["mililitre"] = ("ML", 1),
            ["milliliter"] = ("ML", 1),
            ["cc"] = ("ML", 1),
            ["cl"] = ("ML", 10),
            ["dl"] = ("ML", 100),
            ["lt"] = ("ML", 1000),
            ["l"] = ("ML", 1000),
            ["litre"] = ("ML", 1000),
            ["liter"] = ("ML", 1000),
            ["litresi"] = ("ML", 1000),

            ["kb"] = ("MB", 1m / 1024m),
            ["mb"] = ("MB", 1m),
            ["gb"] = ("MB", 1024m),
            ["tb"] = ("MB", 1024m * 1024m),

            ["mm"] = ("MM", 1m),
            ["milimetre"] = ("MM", 1m),
            ["cm"] = ("MM", 10m),
            ["santimetre"] = ("MM", 10m),
            ["m"] = ("MM", 1000m),
            ["metre"] = ("MM", 1000m),
            ["inç"] = ("MM", 25.4m),
            ["inc"] = ("MM", 25.4m),
            ["\""] = ("MM", 25.4m),
        };

    public static bool IsUnit(string? unit) => !string.IsNullOrWhiteSpace(unit) && UnitMap.ContainsKey(unit);

    private static readonly Regex RangeSplitRegex = new(@"\s*(?:-|–|—|\sto\s|/)\s*", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(@"(\d+(?:[.,]\d+)?)", RegexOptions.Compiled);

    public readonly record struct MagnitudeKey(double MinValue, double MaxValue, bool HasNumeric);

    private const string UnitPattern = @"kilogram|kilo|gram|gr\.?|kg\.?|mililitre|milliliter|litre|liter|ml\.?|cc|cl|lt\.?|l|g";

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static (decimal? value, string? unit, int packQuantity) Parse(ProductDto product)
    {
        return Parse(product.Title, product.SourceUnitType, product.SourceUnitAmount);
    }

    public static (decimal? value, string? unit, int packQuantity) Parse(string? title, string? sourceUnitType = null, decimal? sourceUnitAmount = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null, 1);

        if (title.Contains("yumurta", StringComparison.OrdinalIgnoreCase))
            return ExtractSize(title);

        if (!string.IsNullOrWhiteSpace(sourceUnitType) && sourceUnitAmount is > 0)
        {
            var clean = sourceUnitType.Trim().TrimEnd('.', ',', ';').ToLowerInvariant();
            if (UnitMap.TryGetValue(clean, out var mapping))
            {
                return (sourceUnitAmount.Value * mapping.Multiplier, mapping.CanonicalUnit, 1);
            }
        }

        return ExtractSize(title);
    }

    public static (decimal? value, string? unit, int packQuantity) ExtractSize(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null, 1);

        if (title.Contains("yumurta", StringComparison.OrdinalIgnoreCase))
        {
            var eggCountMatch = Regex.Match(title, @"(\d+)\s*(?:adet\b|['’]?\s?l[iIıİuüÜ]\b)", Opts);
            return eggCountMatch.Success
                ? (decimal.Parse(eggCountMatch.Groups[1].Value), "ADET", 1)
                : (null, null, 1);
        }

        var packMatch = Regex.Match(title, $@"(\d+)\s*(?:[xX*]|adet\s*[xX*]?)\s*(\d+[.,]?\d*)\s*({UnitPattern})\b", Opts);
        if (packMatch.Success)
        {
            var packQty = int.Parse(packMatch.Groups[1].Value);
            var (value, unit) = Normalize(packMatch.Groups[2].Value, packMatch.Groups[3].Value);
            return (value, unit, packQty);
        }

        var packWordMatch = Regex.Match(title, $@"(\d+)\s*['’]?\s*l[iIıİuüÜ]\s*(?:paket\s*)?(\d+[.,]?\d*)\s*({UnitPattern})\b", Opts);
        if (packWordMatch.Success)
        {
            var packQty = int.Parse(packWordMatch.Groups[1].Value);
            var (value, unit) = Normalize(packWordMatch.Groups[2].Value, packWordMatch.Groups[3].Value);
            return (value, unit, packQty);
        }

        var singleMatch = Regex.Match(title, $@"(\d+[.,]?\d*)\s*({UnitPattern})\b", Opts);
        if (singleMatch.Success)
        {
            var (value, unit) = Normalize(singleMatch.Groups[1].Value, singleMatch.Groups[2].Value);
            return (value, unit, 1);
        }

        var countMatch = Regex.Match(title, @"(\d+)\s*(?:adet\b|['’]?\s?l[iIıİuüÜ]\b)", Opts);
        if (countMatch.Success)
        {
            var count = decimal.Parse(countMatch.Groups[1].Value);
            return (count, "ADET", 1);
        }

        var bareUnitMatch = Regex.Match(title, @"\b(kg|kilo|kilogram|lt|litre)\b", Opts);
        if (bareUnitMatch.Success)
        {
            var isWeight = bareUnitMatch.Groups[1].Value.ToLowerInvariant() is "kg" or "kilo" or "kilogram";
            return (1000m, isWeight ? "G" : "ML", 1);
        }

        return (null, null, 1);
    }

    private static (decimal value, string unit) Normalize(string rawValue, string rawUnit)
    {
        var value = decimal.Parse(rawValue.Replace(",", "."),
            System.Globalization.CultureInfo.InvariantCulture);

        var cleanedUnit = rawUnit.TrimEnd('.').ToLowerInvariant();
        if (UnitMap.TryGetValue(cleanedUnit, out var mapping))
        {
            return (value * mapping.Multiplier, mapping.CanonicalUnit);
        }

        return (value, cleanedUnit.ToUpperInvariant());
    }

    public static double GetUnitMultiplier(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return 1.0;
        var clean = unit.Trim().TrimEnd('.', ',', ';').ToLowerInvariant();
        return UnitMap.TryGetValue(clean, out var mapping) ? (double)mapping.Multiplier : 1.0;
    }

    public static MagnitudeKey GetMagnitudeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new MagnitudeKey(double.MaxValue, double.MaxValue, false);

        var text = raw.Trim().ToLower(new CultureInfo("tr-TR"));

        bool isUnder = text.Contains("altı") || text.Contains("alti") || text.Contains("<=") || text.Contains("kadar");
        bool isOver = text.Contains("üzeri") || text.Contains("uzeri") || text.Contains(">=") || text.Contains("artı") || text.Contains("arti") || text.Contains("+");

        var parts = RangeSplitRegex.Split(text);
        if (parts.Length == 2)
        {
            var (v1, u1) = ExtractNumberAndUnit(parts[0]);
            var (v2, u2) = ExtractNumberAndUnit(parts[1]);

            if (v1.HasValue || v2.HasValue)
            {
                var effectiveUnit1 = !string.IsNullOrEmpty(u1) ? u1 : u2;
                var effectiveUnit2 = !string.IsNullOrEmpty(u2) ? u2 : u1;

                double min = v1.HasValue ? v1.Value * GetUnitMultiplier(effectiveUnit1) : 0;
                double max = v2.HasValue ? v2.Value * GetUnitMultiplier(effectiveUnit2) : double.MaxValue;

                if (min > max) (min, max) = (max, min);
                return new MagnitudeKey(min, max, true);
            }
        }

        var (val, unit) = ExtractNumberAndUnit(text);
        if (val.HasValue)
        {
            double mult = GetUnitMultiplier(unit);
            double baseVal = val.Value * mult;

            if (isUnder)
                return new MagnitudeKey(0, baseVal, true);

            if (isOver)
                return new MagnitudeKey(baseVal, double.MaxValue, true);

            return new MagnitudeKey(baseVal, baseVal, true);
        }

        return new MagnitudeKey(double.MaxValue, double.MaxValue, false);
    }

    private static (double? Value, string? Unit) ExtractNumberAndUnit(string segment)
    {
        var match = NumberPattern.Match(segment);
        if (!match.Success) return (null, null);

        var numStr = match.Groups[1].Value.Replace(',', '.');
        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return (null, null);

        var unitPart = segment[(match.Index + match.Length)..].Trim();
        var words = unitPart.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unit = words.Length > 0 ? words[0] : "";

        return (num, unit);
    }
}