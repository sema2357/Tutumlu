using System.Text.RegularExpressions;
using PriceAggregator.Core.Helpers;

namespace PriceAggregator.Core.Categorization;

public static class TechAttributeExtractor
{
    // Dahili Hafıza / Depolama (ROM/SSD/Depolama)
    // 32 GB, 64 GB, 128 GB, 256 GB, 512 GB, 1 TB, 2 TB
    // Tablet/Telefon: "8/256 GB", "4/64 GB", "128 GB"
    // Laptop: "512 GB SSD", "1 TB SSD"
    private static readonly Regex RomRegex = new(
        @"(?:(?:(?<ram>\d+)\s*\/\s*(?<rom>\d+)\s*(?<romUnit>GB|TB))|(?<romOnly>\d+)\s*(?<romOnlyUnit>GB|TB)\s*(?:SSD|HDD|Hafıza|Depolama|ROM|Wi-Fi|Tablet|Cep)?(?:\b|$))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // RAM / Bellek
    // Telefon/Tablet formatı: "8/256 GB" -> RAM: 8 GB
    // Laptop formatı: "16 GB RAM", "8 GB Bellek"
    private static readonly Regex RamExplicitRegex = new(
        @"\b(?<ram>\d+)\s*GB\s*(?:RAM|Bellek|DDR4|DDR5)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlashRamRomRegex = new(
        @"\b(?<ram>\d{1,2})\s*\/\s*(?<rom>\d{2,4})\s*(?<unit>GB|TB)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bağlantı Türü (5G, 4.5G, LTE, Wi-Fi, Bluetooth)
    private static readonly Regex ConnectionRegex = new(
        @"\b(5G|4\.5G|4G|LTE|Wi-Fi|Wifi)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Ekran Boyutu (örn: 15.6", 14", 11.5'', 11")
    private static readonly Regex ScreenSizeRegex = new(
        @"\b(?<size>\d{1,2}(?:\.\d{1,2})?)\s*(?:""|''|inç|inch)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Renkler
    private static readonly (string CanonicalColor, string[] Patterns)[] Colors =
    [
        ("Siyah", ["siyah", "black", "gece yarisi", "midnight", "phantom black", "uzay grisi", "space gray", "antrasit"]),
        ("Beyaz", ["beyaz", "white", "yildiz isigi", "starlight", "kutup beyazi"]),
        ("Gri", ["gri", "grey", "gray", "gumus", "silver", "titanyum"]),
        ("Mavi", ["mavi", "blue", "gok mavisi", "lacivert", "navy"]),
        ("Yeşil", ["yesil", "green", "mint", "zeytin"]),
        ("Kırmızı", ["kirmizi", "red"]),
        ("Sarı", ["sari", "yellow", "gold", "altin"]),
        ("Pembe", ["pembe", "pink", "rose gold", "rose"]),
        ("Mor", ["mor", "purple", "violet", "lavanta"])
    ];

    public static List<(string AttributeName, string AttributeValue)> Extract(string? title)
    {
        var results = new List<(string AttributeName, string AttributeValue)>();
        if (string.IsNullOrWhiteSpace(title)) return results;

        var normalizedTitle = TurkishTextHelper.NormalizeToAscii(title).ToLowerInvariant();

        // 1. RAM & Dahili Hafıza Kontrolü
        string? ramVal = null;
        string? romVal = null;

        // Önce "8/256 GB" biçimi (Telefon / Tablet standardı)
        var slashMatch = SlashRamRomRegex.Match(title);
        if (slashMatch.Success)
        {
            var r = int.Parse(slashMatch.Groups["ram"].Value);
            var m = int.Parse(slashMatch.Groups["rom"].Value);
            var unit = slashMatch.Groups["unit"].Value.ToUpperInvariant();

            // Geçerli tipik RAM değerleri: 2, 3, 4, 6, 8, 12, 16, 18, 24, 32
            if (r is 2 or 3 or 4 or 6 or 8 or 12 or 16 or 18 or 24 or 32)
            {
                ramVal = $"{r} GB";
                romVal = $"{m} {unit}";
            }
        }

        // Açıkça "16 GB RAM" yazıyorsa
        var ramExplicitMatch = RamExplicitRegex.Match(title);
        if (ramExplicitMatch.Success)
        {
            ramVal = $"{ramExplicitMatch.Groups["ram"].Value} GB";
        }

        // SSD / Dahili Hafıza ("512 GB SSD", "128 GB" vb.)
        if (romVal == null)
        {
            var romMatch = Regex.Match(title, @"\b(?<val>32|64|128|256|512|1024|1|2)\s*(?<unit>GB|TB)\s*(?:SSD|HDD|Hafıza|Depolama|ROM)?\b", RegexOptions.IgnoreCase);
            if (romMatch.Success)
            {
                var valStr = romMatch.Groups["val"].Value;
                var unit = romMatch.Groups["unit"].Value.ToUpperInvariant();

                // Eğer "16 GB RAM" olan değerle aynı değilse
                if (ramVal == null || !ramVal.StartsWith(valStr))
                {
                    romVal = $"{valStr} {unit}";
                }
            }
        }

        if (!string.IsNullOrEmpty(romVal))
            results.Add(("Dahili Hafiza", romVal));

        if (!string.IsNullOrEmpty(ramVal))
            results.Add(("RAM", ramVal));

        // 2. Renk Kontrolü
        foreach (var (canonicalColor, patterns) in Colors)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(normalizedTitle, $@"\b{Regex.Escape(pattern)}\b"))
                {
                    results.Add(("Renk", canonicalColor));
                    break;
                }
            }
            if (results.Any(r => r.AttributeName == "Renk")) break;
        }

        // 3. Bağlantı Türü (5G, Wi-Fi vb.)
        var connMatch = ConnectionRegex.Match(title);
        if (connMatch.Success)
        {
            var connVal = connMatch.Groups[1].Value.ToUpperInvariant();
            if (connVal == "WIFI") connVal = "Wi-Fi";
            results.Add(("Baglanti", connVal));
        }

        // 4. Ekran / Kasa Boyutu (15.6", 14", 11.5" vb.)
        var sizeMatch = ScreenSizeRegex.Match(title);
        if (sizeMatch.Success)
        {
            results.Add(("Boyut", $"{sizeMatch.Groups["size"].Value}\""));
        }

        return results;
    }
}
