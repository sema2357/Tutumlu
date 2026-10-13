using System.Text;
using System.Text.Json;

namespace PriceAggregator.Core.Scrapers;

public class MarketFiyatiScraper : IProductScraper
{
    public string Name => "marketfiyati";
    public ScraperSector Sector => ScraperSector.Market;

    private readonly HttpClient _httpClient;

    public string GenerateSlug(string title) => Helpers.TurkishTextHelper.GenerateSlug(title, string.Empty);

    public MarketFiyatiScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.DefaultRequestHeaders.Remove("Accept");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Remove("Accept-Language");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Remove("Origin");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://marketfiyati.org.tr");
        _httpClient.DefaultRequestHeaders.Remove("Referer");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://marketfiyati.org.tr/");
    }

    private const int PageSize = 24;

    // The request body's "pages" field is 0-indexed and genuinely paginates (live-confirmed:
    // "su" reports numberOfFound=170 at size 24, i.e. ~8 pages) — this used to be hardcoded to
    // 0, so every search silently stopped after the first 24 results regardless of how many
    // more the store actually had. Capped rather than walked to exhaustion for the same reason
    // as MigrosScraper.MaxPages.
    private const int MaxPages = 5;

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        var results = new List<ProductDto>();

        for (var page = 0; page < MaxPages; page++)
        {
            var payload = new
            {
                keywords = searchTerm,
                pages = page,
                size = PageSize,
                latitude = 41.08037598,
                longitude = 28.79330796,
                distance = 1,
                depots = new[] { "bim-U153", "a101-D021", "a101-I398", "bim-U160", "a101-F979" }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync("https://api.marketfiyati.org.tr/api/v2/search", content);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var products = document.RootElement.GetProperty("content");

            if (products.GetArrayLength() == 0) break;

            foreach (var p in products.EnumerateArray())
            {
                decimal price = 0;
                if (p.TryGetProperty("productDepotInfoList", out var depots) && depots.GetArrayLength() > 0)
                {
                    price = depots[0].GetProperty("price").GetDecimal();
                }

                var idString = p.GetProperty("id").GetString() ?? "";

                results.Add(new ProductDto
                {
                    ExternalId = StableHash(idString),
                    Title = p.GetProperty("title").GetString() ?? "",
                    Brand = p.TryGetProperty("brand", out var brandEl) ? brandEl.GetString() : null,
                    ImageUrl = p.TryGetProperty("imageUrl", out var image) ? image.GetString() : null,
                    Price = price,
                    RegularPrice = price,
                    ProductUrl = $"https://marketfiyati.org.tr/detay/{idString}/{GenerateSlug(p.GetProperty("title").GetString() ?? "")}",
                    // MarketFiyati only gives plain-text category names (no ids/slugs) —
                    // main_category is the leaf, menu_category the root; no middle tier.
                    CategoryName = p.TryGetProperty("main_category", out var mainCat) ? mainCat.GetString() : null,
                    RootCategoryName = p.TryGetProperty("menu_category", out var menuCat) ? menuCat.GetString() : null,
                    CategoryPath = ParseMarketFiyatiCategoryPath(p)
                });
            }

            var numberOfFound = document.RootElement.TryGetProperty("numberOfFound", out var nf) ? nf.GetInt32() : 0;
            if ((page + 1) * PageSize >= numberOfFound) break;
        }

        return results;
    }

    private static long StableHash(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt64(bytes, 0);
    }

    private static string FormatStoreName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "";
        return rawName.ToLowerInvariant() switch
        {
            "bim" => "BİM",
            "a101" => "A101",
            "sok" => "Şok",
            "migros" => "Migros",
            "carrefour" => "CarrefourSA",
            var other => char.ToUpperInvariant(other[0]) + other.Substring(1)
        };
    }

    private static List<string> ParseMarketFiyatiCategoryPath(JsonElement p)
    {
        var path = new List<string>();

        var menuCat = p.TryGetProperty("menu_category", out var menc) ? menc.GetString()?.Trim() : null;
        if (!string.IsNullOrWhiteSpace(menuCat)) path.Add(menuCat);

        if (p.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cats.EnumerateArray())
            {
                var val = c.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(val)) continue;
                // Strip numeric prefix codes like "0301-su" or "030101-5 Litre"
                var cleaned = System.Text.RegularExpressions.Regex.Replace(val, @"^\d+[-_]\s*", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleaned) && !path.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                {
                    path.Add(cleaned);
                }
            }
        }

        var mainCat = p.TryGetProperty("main_category", out var mc) ? mc.GetString()?.Trim() : null;
        if (!string.IsNullOrWhiteSpace(mainCat) && !path.Contains(mainCat, StringComparer.OrdinalIgnoreCase))
        {
            path.Add(mainCat);
        }

        return path;
    }
}