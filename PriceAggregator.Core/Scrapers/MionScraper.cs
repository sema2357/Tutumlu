using System.Text.Json;

namespace PriceAggregator.Core.Scrapers;

public class MionScraper : IProductScraper, IProductSpecScraper, ICategoryScraper
{
    public string Name => "mion";
    public ScraperSector Sector => ScraperSector.Mobilya;

    private readonly HttpClient _httpClient;

    public MionScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.DefaultRequestHeaders.Remove("Accept");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Remove("Accept-Language");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Remove("Referer");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.mion.com.tr/");
    }

    // Already stops early on an empty page (see FetchProductsAsync below) — raising the ceiling
    // only lets broader terms pull more of what's actually there.
    private const int MaxPages = 5;

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        var encodedSearch = Uri.EscapeDataString(searchTerm);
        var results = new List<ProductDto>();

        for (int page = 1; page <= MaxPages; page++)
        {
            var url =
                $"https://www.migros.com.tr/rest/mion/search/screens/products?q={encodedSearch}&page={page}";

            var response = await _httpClient.GetStringAsync(url);

            using var json = JsonDocument.Parse(response);

            var searchInfo = json.RootElement
                .GetProperty("data")
                .GetProperty("searchInfo");
            var products = searchInfo.GetProperty("storeProductInfos");

            if (products.GetArrayLength() == 0) break;

            foreach (var p in products.EnumerateArray())
            {
                string? imageUrl = null;

                if (p.TryGetProperty("images", out var images) &&
                    images.GetArrayLength() > 0)
                {
                    imageUrl = images[0]
                        .GetProperty("urls")
                        .GetProperty("PRODUCT_DETAIL")
                        .GetString();
                }

                string brand = p.TryGetProperty("brand", out var brandEl) && brandEl.ValueKind == JsonValueKind.Object
                    ? brandEl.GetProperty("name").GetString() ?? ""
                    : "";

                // Mion runs on the same Migros commerce platform, so its product JSON has the
                // identical category/categoryAscendants shape (see MacroCenterScraper, which
                // reuses this same parser for the same reason).
                var (categoryName, parentName, rootName, storeCategoryId, categoryPath) = MigrosScraper.ParseCategory(p);
                var (unitType, unitAmount, sourceUnitPrice) = MigrosScraper.ParseUnitInfo(p);

                results.Add(new ProductDto
                {
                    ExternalId = p.GetProperty("id").GetInt64(),
                    Title = p.GetProperty("name").GetString() ?? "",
                    ImageUrl = imageUrl,
                    Price = p.GetProperty("shownPrice").GetInt32() / 100m,
                    RegularPrice = p.GetProperty("regularPrice").GetInt32() / 100m,
                    ProductUrl = $"https://www.mion.com.tr/urun/{p.GetProperty("prettyName").GetString()}",
                    Brand = brand,
                    CategoryName = categoryName,
                    StoreCategoryId = storeCategoryId,
                    ParentCategoryName = parentName,
                    RootCategoryName = rootName,
                    CategoryPath = categoryPath,
                    SourceUnitType = unitType,
                    SourceUnitAmount = unitAmount,
                    SourceUnitPrice = sourceUnitPrice
                });
            }

            var pageCount = searchInfo.TryGetProperty("pageCount", out var pageCountEl) ? pageCountEl.GetInt32() : 1;
            if (page >= pageCount) break;
        }

        return results;
    }

    public async Task<List<ProductDto>> FetchProductsByCategoryAsync(string storeCategoryId)
    {
        var results = new List<ProductDto>();

        for (int page = 1; page <= MaxPages; page++)
        {
            try
            {
                var url = $"https://www.migros.com.tr/rest/mion/search/screens/products?categories={storeCategoryId}&sirala=akilli-siralama&page={page}";
                var response = await _httpClient.GetStringAsync(url);
                using var json = JsonDocument.Parse(response);

                var searchInfo = json.RootElement.GetProperty("data").GetProperty("searchInfo");
                var products = searchInfo.GetProperty("storeProductInfos");

                if (products.GetArrayLength() == 0) break;

                foreach (var p in products.EnumerateArray())
                {
                    string? imageUrl = null;
                    if (p.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                    {
                        imageUrl = images[0].GetProperty("urls").GetProperty("PRODUCT_DETAIL").GetString();
                    }

                    string brand = p.TryGetProperty("brand", out var brandEl) && brandEl.ValueKind == JsonValueKind.Object
                        ? brandEl.GetProperty("name").GetString() ?? ""
                        : "";

                    var (categoryName, parentName, rootName, id, categoryPath) = MigrosScraper.ParseCategory(p);
                    var (unitType, unitAmount, sourceUnitPrice) = MigrosScraper.ParseUnitInfo(p);

                    results.Add(new ProductDto
                    {
                        ExternalId = p.GetProperty("id").GetInt64(),
                        Title = p.GetProperty("name").GetString() ?? "",
                        ImageUrl = imageUrl,
                        Price = p.GetProperty("shownPrice").GetInt32() / 100m,
                        RegularPrice = p.GetProperty("regularPrice").GetInt32() / 100m,
                        ProductUrl = $"https://www.mion.com.tr/urun/{p.GetProperty("prettyName").GetString()}",
                        Brand = brand,
                        CategoryName = categoryName,
                        StoreCategoryId = id,
                        ParentCategoryName = parentName,
                        RootCategoryName = rootName,
                        CategoryPath = categoryPath,
                        SourceUnitType = unitType,
                        SourceUnitAmount = unitAmount,
                        SourceUnitPrice = sourceUnitPrice
                    });
                }

                var pageCount = searchInfo.TryGetProperty("pageCount", out var pageCountEl) ? pageCountEl.GetInt32() : 1;
                if (page >= pageCount) break;
            }
            catch (Exception ex)
            {
                Logger.Log($"[Mion] Error in FetchProductsByCategoryAsync: {ex.Message}");
                break;
            }
        }

        return results;
    }

    public async Task<List<PriceAggregator.Core.Data.StoreCategoryDto>> FetchStoreCategoriesAsync()
    {
        var results = new List<PriceAggregator.Core.Data.StoreCategoryDto>();

        try
        {
            var url = "https://www.migros.com.tr/rest/mion/categories";
            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);

            if (json.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var topLevel in dataArray.EnumerateArray())
                {
                    ParseMigrosCategoryTree(topLevel, null, results);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Mion] Failed to fetch categories: {ex.Message}");
        }

        return results;
    }

    private void ParseMigrosCategoryTree(JsonElement categoryNode, string? parentPrettyName, List<PriceAggregator.Core.Data.StoreCategoryDto> results)
    {
        if (!categoryNode.TryGetProperty("data", out var data)) return;

        var name = data.TryGetProperty("name", out var n) ? n.GetString() : null;
        var prettyName = data.TryGetProperty("prettyName", out var pn) ? pn.GetString() : null;
        
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(prettyName))
        {
            results.Add(new PriceAggregator.Core.Data.StoreCategoryDto
            {
                StoreName = Name,
                StoreCategoryName = name,
                StoreCategoryId = prettyName,
                ParentStoreCategoryId = parentPrettyName
            });
        }

        if (categoryNode.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                ParseMigrosCategoryTree(child, prettyName, results);
            }
        }
    }

    // Mion runs on the Migros platform under a "/mion/" prefix (see FetchProductsAsync above),
    // and its product-detail endpoint follows the same pattern: same propertyInfosMap shape as
    // Migros/MacroCenter (see MigrosScraper.ParseSpecs), just on Migros' own host.
    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        try
        {
            var url = $"https://www.migros.com.tr/rest/mion/products/{product.ExternalId}";
            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);
            return json.RootElement.TryGetProperty("data", out var data) ? MigrosScraper.ParseSpecs(data) : [];
        }
        catch (Exception ex) when (!MigrosScraper.IsRateLimited(ex))
        {
            Logger.Log($"[Mion] Failed to fetch specs for product {product.ExternalId}: {ex.Message}");
            return [];
        }
    }
}