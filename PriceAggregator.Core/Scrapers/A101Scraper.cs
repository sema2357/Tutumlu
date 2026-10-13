using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Scrapers;

public class A101Scraper : IProductScraper
{
    public string Name => "a101";
    public ScraperSector Sector => ScraperSector.Market;

    private readonly HttpClient _httpClient;
    private const int PageSize = 60;
    private const int MaxPages = 3;

    public A101Scraper(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Remove("Accept");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Remove("Accept-Language");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr,en-US;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Remove("Origin");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.a101.com.tr");
        _httpClient.DefaultRequestHeaders.Remove("Referer");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.a101.com.tr/");
    }

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        var results = new List<ProductDto>();
        if (string.IsNullOrWhiteSpace(searchTerm))
            return results;

        for (int page = 1; page <= MaxPages; page++)
        {
            try
            {
                var payload = new
                {
                    query = searchTerm,
                    page_number = page,
                    row_per_page = PageSize,
                    filter = new[]
                    {
                        new
                        {
                            field = "locations",
                            child = "location",
                            type = "value",
                            values = new[] { "VS032-SLOT" }
                        }
                    },
                    sort = Array.Empty<object>(),
                    hids = ""
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://a101-kapida-prod.wawlabs.com/search")
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Log($"[A101] Search returned status {response.StatusCode} for query: '{searchTerm}'");
                    break;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);

                var productsArray = FindProductsArray(doc.RootElement);
                if (productsArray == null || productsArray.Value.GetArrayLength() == 0)
                {
                    break;
                }

                int countInPage = 0;
                foreach (var p in productsArray.Value.EnumerateArray())
                {
                    countInPage++;

                    // Parse ID
                    long externalId = 0;
                    string idStr = "";
                    if (p.TryGetProperty("id", out var idProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.String)
                        {
                            idStr = idProp.GetString() ?? "";
                            if (!long.TryParse(idStr, out externalId))
                                externalId = StableHash(idStr);
                        }
                        else if (idProp.ValueKind == JsonValueKind.Number)
                        {
                            externalId = idProp.GetInt64();
                            idStr = externalId.ToString();
                        }
                    }
                    if (externalId == 0 && !string.IsNullOrEmpty(idStr))
                    {
                        externalId = StableHash(idStr);
                    }

                    // Parse Title
                    string title = p.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(title) && p.TryGetProperty("title_org", out var titleOrgEl))
                    {
                        title = titleOrgEl.GetString() ?? "";
                    }
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    // Parse Brand
                    string? brand = p.TryGetProperty("brand", out var brandEl) && brandEl.ValueKind == JsonValueKind.String
                        ? brandEl.GetString()
                        : null;

                    // Parse Prices
                    decimal price = 0;
                    if (p.TryGetProperty("price", out var priceEl) && priceEl.ValueKind == JsonValueKind.Number && priceEl.TryGetDecimal(out var parsedPrice))
                    {
                        price = parsedPrice;
                    }
                    if (price == 0 && p.TryGetProperty("locations", out var locs) && locs.ValueKind == JsonValueKind.Array && locs.GetArrayLength() > 0)
                    {
                        var firstLoc = locs[0];
                        if (firstLoc.TryGetProperty("price", out var lp) && lp.ValueKind == JsonValueKind.Number && lp.TryGetDecimal(out var lpv))
                            price = lpv;
                    }

                    decimal regularPrice = price;
                    if (p.TryGetProperty("old_price", out var oldPriceEl) && oldPriceEl.ValueKind == JsonValueKind.Number && oldPriceEl.TryGetDecimal(out var op) && op > 0)
                    {
                        regularPrice = op;
                    }
                    else if (p.TryGetProperty("locations", out var locs2) && locs2.ValueKind == JsonValueKind.Array && locs2.GetArrayLength() > 0)
                    {
                        var firstLoc = locs2[0];
                        if (firstLoc.TryGetProperty("oldprice", out var olp) && olp.ValueKind == JsonValueKind.Number && olp.TryGetDecimal(out var olpv) && olpv > 0)
                            regularPrice = olpv;
                    }

                    if (regularPrice < price)
                        regularPrice = price;

                    // Parse Image
                    string? imageUrl = null;
                    if (p.TryGetProperty("images", out var imagesEl) && imagesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var img in imagesEl.EnumerateArray())
                        {
                            if (img.TryGetProperty("imageType", out var typeEl) && typeEl.GetString() == "product")
                            {
                                if (img.TryGetProperty("url", out var u) && !string.IsNullOrWhiteSpace(u.GetString()))
                                {
                                    imageUrl = u.GetString();
                                    break;
                                }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(imageUrl) && imagesEl.GetArrayLength() > 0)
                        {
                            var first = imagesEl[0];
                            if (first.TryGetProperty("url", out var u))
                                imageUrl = u.GetString();
                        }
                    }

                    // Parse URL
                    string productUrl = "";
                    if (p.TryGetProperty("seoUrl", out var seoUrlEl) && !string.IsNullOrWhiteSpace(seoUrlEl.GetString()))
                    {
                        productUrl = seoUrlEl.GetString()!;
                    }
                    else if (p.TryGetProperty("link", out var linkEl) && !string.IsNullOrWhiteSpace(linkEl.GetString()))
                    {
                        productUrl = linkEl.GetString()!;
                    }
                    else if (!string.IsNullOrEmpty(idStr))
                    {
                        productUrl = $"https://www.a101kapida.com/product/{idStr}";
                    }

                    // Parse Category
                    string? categoryName = null;
                    string? parentCategoryName = null;
                    string? rootCategoryName = null;

                    if (p.TryGetProperty("category_org", out var catOrgEl) && !string.IsNullOrWhiteSpace(catOrgEl.GetString()))
                    {
                        var parts = catOrgEl.GetString()!.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 1)
                        {
                            categoryName = parts[0];
                        }
                        else if (parts.Length == 2)
                        {
                            rootCategoryName = parts[0];
                            categoryName = parts[1];
                        }
                        else if (parts.Length >= 3)
                        {
                            rootCategoryName = parts[0];
                            parentCategoryName = parts[^2];
                            categoryName = parts[^1];
                        }
                    }
                    else if (p.TryGetProperty("category", out var catEl) && catEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(catEl.GetString()))
                    {
                        categoryName = catEl.GetString();
                    }

                    string? storeCategoryId = p.TryGetProperty("category_id", out var catIdEl) && catIdEl.ValueKind == JsonValueKind.String ? catIdEl.GetString() : null;

                    // Parse Unit / Measure
                    string? unitType = p.TryGetProperty("salesUnitOfMeasure", out var su) && su.ValueKind == JsonValueKind.String ? su.GetString() : null;
                    decimal? unitPrice = p.TryGetProperty("unitPrice", out var upEl) && upEl.ValueKind == JsonValueKind.Number && upEl.TryGetDecimal(out var upVal) ? upVal : null;

                    results.Add(new ProductDto
                    {
                        ExternalId = externalId,
                        Title = title,
                        ImageUrl = imageUrl,
                        Price = price,
                        RegularPrice = regularPrice,
                        ProductUrl = productUrl,
                        Brand = brand,
                        CategoryName = categoryName,
                        ParentCategoryName = parentCategoryName,
                        RootCategoryName = rootCategoryName,
                        StoreCategoryId = storeCategoryId,
                        StoreName = "A101",
                        SourceUnitType = unitType,
                        SourceUnitPrice = unitPrice
                    });
                }

                if (countInPage < PageSize)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[A101] Error fetching page {page} for '{searchTerm}': {ex.Message}");
                break;
            }
        }

        return results;
    }

    private static JsonElement? FindProductsArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() > 0)
            {
                var first = root[0];
                if (first.ValueKind == JsonValueKind.Object)
                {
                    if (first.TryGetProperty("title", out _) || first.TryGetProperty("id", out _) || first.TryGetProperty("price", out _))
                        return root;

                    // If it's an array of containers (like WAW Labs root.res[0]), inspect inside
                    var nested = FindProductsArray(first);
                    if (nested != null) return nested;
                }
            }
            return root;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            string[] candidates = ["res", "data", "products", "results", "items", "content", "hits"];
            foreach (var name in candidates)
            {
                if (root.TryGetProperty(name, out var el))
                {
                    var nested = FindProductsArray(el);
                    if (nested != null) return nested;
                }
            }

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array || prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var nested = FindProductsArray(prop.Value);
                    if (nested != null) return nested;
                }
            }
        }

        return null;
    }

    private static long StableHash(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt64(bytes, 0);
    }
}
