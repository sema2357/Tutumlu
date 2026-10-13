using System.Text.Json;
using System.Text.RegularExpressions;
using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Scrapers;


public class IkeaScraper : IProductScraper, IProductSpecScraper
{
    public string Name => "ikea";
    public ScraperSector Sector => ScraperSector.Mobilya;

    private readonly HttpClient _httpClient;

    public IkeaScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0.0.0 Safari/537.36"
            );
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
        {
            _httpClient.DefaultRequestHeaders.Add(
                "Accept",
                "application/json, text/plain, */*"
            );
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("Accept-Language"))
        {
            _httpClient.DefaultRequestHeaders.Add(
                "Accept-Language",
                "tr-TR,tr;q=0.9"
            );
        }

        _httpClient.DefaultRequestHeaders.Remove("Referer");
        _httpClient.DefaultRequestHeaders.Add(
            "Referer",
            "https://www.ikea.com.tr/"
        );

    }

    private const int PageSize = 24;
    // Already stops early once the store's own totalPages is reached (see below) — raising the
    // ceiling only lets broader terms pull more of what's actually there instead of leaving it
    // on the table, it doesn't force extra requests on narrow terms.
    private const int MaxPages = 5;

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        var results = new List<ProductDto>();

        for (int page = 1; page <= MaxPages; page++)
        {
            var url =
                "https://frontendapi.ikea.com.tr/api/search/products" +
                $"?Keyword={Uri.EscapeDataString(searchTerm)}" +
                "&language=tr" +
                "&IncludeFilters=false" +
                "&StoreCode=331" +
                "&sortby=None" +
                $"&page={page}" +
                $"&size={PageSize}" +
                "&SearchIn=product" +
                "&IncludeColorVariants=true";

            var response = await _httpClient.GetStringAsync(url);

            using var json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty("products", out var products) ||
                products.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var p in products.EnumerateArray())
            {
                string? imageUrl = null;

                if (
                    p.TryGetProperty("images", out var images) &&
                    images.ValueKind == JsonValueKind.Array &&
                    images.GetArrayLength() > 0 &&
                    images[0].TryGetProperty("image", out var imgProp)
                )
                {
                    imageUrl = imgProp.GetString();
                }

                var title = p.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                var subTitle = p.TryGetProperty("subTitle", out var stProp) ? stProp.GetString() ?? "" : "";

                var productName = $"{title} {subTitle}".Trim();

                var productPath = p.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "" : "";
                var productUrl = string.IsNullOrWhiteSpace(productPath)
                    ? "https://www.ikea.com.tr"
                    : (productPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? productPath
                        : $"https://www.ikea.com.tr{productPath}");

                long id = 0;
                if (p.TryGetProperty("id", out var idProp))
                {
                    if (idProp.ValueKind == JsonValueKind.Number)
                        id = idProp.GetInt64();
                    else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var parsedId))
                        id = parsedId;
                }

                decimal price = 0;
                if (p.TryGetProperty("price", out var priceProp) && priceProp.ValueKind == JsonValueKind.Number)
                {
                    price = priceProp.GetDecimal();
                }

                decimal regularPrice = price;
                if (p.TryGetProperty("crossPrice", out var crossPriceProp) && crossPriceProp.ValueKind == JsonValueKind.Number)
                {
                    regularPrice = crossPriceProp.GetDecimal();
                }
                else if (p.TryGetProperty("regularPrice", out var regPriceProp) && regPriceProp.ValueKind == JsonValueKind.Number)
                {
                    regularPrice = regPriceProp.GetDecimal();
                }

                // Each product can appear under several parallel "navigations" (e.g. both a
                // room-based tree and a product-type tree) — each with its own "breadcrumb"
                // array (root-first). Prefer whichever breadcrumb starts at "Mobilyalar", the
                // general furniture root, so products land in one consistent tree instead of
                // whichever tree happened to be listed first.
                var categoryPath = new List<string>();
                string? categoryName = null, parentCategoryName = null, rootCategoryName = null;
                if (p.TryGetProperty("navigations", out var navs) && navs.ValueKind == JsonValueKind.Array)
                {
                    JsonElement? chosenBreadcrumb = null;
                    foreach (var nav in navs.EnumerateArray())
                    {
                        if (!nav.TryGetProperty("breadcrumb", out var bc) || bc.ValueKind != JsonValueKind.Array || bc.GetArrayLength() == 0)
                            continue;

                        var firstName = bc[0].TryGetProperty("name", out var fn) ? fn.GetString() : null;
                        if (firstName == "Mobilyalar") { chosenBreadcrumb = bc; break; }
                        chosenBreadcrumb ??= bc;
                    }

                    if (chosenBreadcrumb is JsonElement bcEl)
                    {
                        var names = bcEl.EnumerateArray()
                            .Select(b => b.TryGetProperty("name", out var n) ? n.GetString() : null)
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .Select(n => n!.Trim())
                            .ToList();

                        if (names.Count >= 3)
                        {
                            rootCategoryName = names[0];
                            parentCategoryName = names[1];
                            categoryName = names[^1];
                        }
                        else if (names.Count == 2)
                        {
                            parentCategoryName = names[0];
                            categoryName = names[1];
                        }
                        else if (names.Count == 1)
                        {
                            categoryName = names[0];
                        }

                        categoryPath = names;
                    }
                }

                // "unitColorName" is IKEA's own structured color/material label for this
                // specific variant (e.g. "paslanmaz çelik-bej" — MatchingService.SplitCompoundValue
                // breaks the hyphenated combo into separate filter values). "familyName" is
                // IKEA's product series/model name (e.g. "MOFALLA", "HEMNES") — a genuine,
                // clean per-product attribute distinct from the title itself.
                var colorName = p.TryGetProperty("unitColorName", out var colorProp) ? colorProp.GetString() : null;
                var seriesName = p.TryGetProperty("familyName", out var familyProp) ? familyProp.GetString() : null;

                Dictionary<string, string>? productAttributes = null;
                if (!string.IsNullOrWhiteSpace(colorName) || !string.IsNullOrWhiteSpace(seriesName))
                {
                    productAttributes = new Dictionary<string, string>();
                    if (!string.IsNullOrWhiteSpace(colorName)) productAttributes["Renk"] = colorName;
                    if (!string.IsNullOrWhiteSpace(seriesName)) productAttributes["Seri"] = seriesName;
                }

                results.Add(new ProductDto
                {
                    ExternalId = id,
                    Title = productName,
                    ImageUrl = imageUrl,
                    Price = price,
                    RegularPrice = regularPrice,
                    ProductUrl = productUrl,
                    // IKEA's search API has no brand field — every product it sells is IKEA's
                    // own private label, so the store name itself is the real brand.
                    Brand = "IKEA",
                    CategoryName = categoryName,
                    ParentCategoryName = parentCategoryName,
                    RootCategoryName = rootCategoryName,
                    CategoryPath = categoryPath,
                    Attributes = productAttributes
                });
            }

            var total = json.RootElement.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number
                ? totalEl.GetInt32()
                : 0;
            if (products.GetArrayLength() == 0 || results.Count >= total) break;
        }

        return results;
    }

    // The search API (FetchProductsAsync above) has no detail endpoint of its own — dimensions
    // and the per-component material breakdown only exist on the product's own rendered page,
    // under two accordion sections ("Ebatlar" / measurements, "Malzeme ve Çevreye Duyarlılık" /
    // material) that share the same "<p>Label: Value</p>" shape, so one regex pulls pairs out
    // of either block once each block's own HTML is isolated.
    private static readonly Regex DimensionsBlockRegex = new(
        @"dimensions-detail"">(?<body>.*?)</div>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex MaterialBlockRegex = new(
        @"accordion-content-title"">Malzeme</h4>\s*(?<body>.*?)</div>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LabelValuePairRegex = new(
        @"<p>(?<name>[^<:]+):\s*(?<value>[^<]+)</p>", RegexOptions.Compiled);

    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        if (string.IsNullOrWhiteSpace(product.ProductUrl)) return [];

        try
        {
            var html = await _httpClient.GetStringAsync(product.ProductUrl);
            var specs = new List<ProductSpec>();

            AppendPairs(html, DimensionsBlockRegex, "Ölçüler", specs);
            AppendPairs(html, MaterialBlockRegex, "Malzeme", specs);

            return specs;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Ikea] Failed to fetch specs for product {product.ExternalId}: {ex.Message}");
            return [];
        }
    }

    private static void AppendPairs(string html, Regex blockRegex, string specGroup, List<ProductSpec> specs)
    {
        var blockMatch = blockRegex.Match(html);
        if (!blockMatch.Success) return;

        foreach (Match pair in LabelValuePairRegex.Matches(blockMatch.Groups["body"].Value))
        {
            var name = pair.Groups["name"].Value.Trim();
            var value = pair.Groups["value"].Value.Trim();
            if (name.Length > 0 && value.Length > 0)
                specs.Add(new ProductSpec(specGroup, name, value));
        }
    }
}
