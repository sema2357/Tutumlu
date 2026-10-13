using System.Text.Json;

namespace PriceAggregator.Core.Scrapers;

public class MacroCenterScraper : IProductScraper, IProductSpecScraper, ICategoryScraper
{
    private readonly HttpClient _httpClient;

    public string Name => "macrocenter";
    public ScraperSector Sector => ScraperSector.Market;

    public MacroCenterScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);

        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.macrocenter.com.tr/");
    }

    // Same Migros-platform search endpoint shape — see MigrosScraper.MaxPages for why this
    // exists (live-confirmed: "su" reports 8 pages / ~213 results at 30/page on MacroCenter).
    private const int MaxPages = 5;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _slugMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _categoryInitLock = new(1, 1);
    private static bool _categoriesLoaded;

    private async Task EnsureCategoriesLoadedAsync()
    {
        if (_categoriesLoaded) return;
        await _categoryInitLock.WaitAsync();
        try
        {
            if (_categoriesLoaded) return;
            await FetchStoreCategoriesAsync();
        }
        finally
        {
            _categoryInitLock.Release();
        }
    }

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        var results = new List<ProductDto>();

        for (var page = 1; page <= MaxPages; page++)
        {
            try
            {
                var url = $"https://www.macrocenter.com.tr/rest/products/search?q={searchTerm}&page-size=30&page={page}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                if (!json.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("storeProductInfos", out var products))
                {
                    Logger.Log("[MacroCenter] Unexpected response shape — no products found.");
                    break;
                }

                if (products.GetArrayLength() == 0) break;

                foreach (var p in products.EnumerateArray())
                {
                    try
                    {
                        results.Add(ParseProduct(p));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[MacroCenter] Skipped one product due to parse error: {ex.Message}");
                    }
                }

                var pageCount = data.TryGetProperty("pageCount", out var pageCountEl) ? pageCountEl.GetInt32() : 1;
                if (page >= pageCount) break;
            }
            catch (HttpRequestException ex)
            {
                Logger.Log($"[MacroCenter] Network error: {ex.Message}");
                break;
            }
            catch (TaskCanceledException)
            {
                Logger.Log("[MacroCenter] Request timed out.");
                break;
            }
            catch (JsonException ex)
            {
                Logger.Log($"[MacroCenter] Failed to parse JSON: {ex.Message}");
                break;
            }
        }

        return results;
    }

    public async Task<List<ProductDto>> FetchProductsByCategoryAsync(string storeCategoryId)
    {
        await EnsureCategoriesLoadedAsync();
        var results = new List<ProductDto>();

        var rawSlug = storeCategoryId;
        if (rawSlug.StartsWith("macrocenter:", StringComparison.OrdinalIgnoreCase))
            rawSlug = rawSlug["macrocenter:".Length..];

        string targetSlug = rawSlug;
        if (_slugMap.TryGetValue(rawSlug, out var directMatch))
        {
            targetSlug = directMatch;
        }
        else
        {
            var leaf = rawSlug.Contains('/') ? rawSlug[(rawSlug.LastIndexOf('/') + 1)..] : rawSlug;
            if (_slugMap.TryGetValue(leaf, out var leafMatch))
            {
                targetSlug = leafMatch;
            }
            else
            {
                var candidate = _slugMap.Keys.FirstOrDefault(k => k.Contains(leaf, StringComparison.OrdinalIgnoreCase) || leaf.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (candidate != null && _slugMap.TryGetValue(candidate, out var candMatch))
                {
                    targetSlug = candMatch;
                }
                else
                {
                    targetSlug = leaf;
                }
            }
        }

        bool isScreenSlug = targetSlug.Contains("-c-") || targetSlug.Contains("-dt-") || targetSlug.Contains("-ptt-");

        for (var page = 1; page <= MaxPages; page++)
        {
            try
            {
                var url = isScreenSlug 
                    ? $"https://www.macrocenter.com.tr/rest/search/screens/{targetSlug}?sayfa={page}"
                    : $"https://www.macrocenter.com.tr/rest/products/search?query={Uri.EscapeDataString(targetSlug.Replace("-", " "))}&sirala=akilli-siralama&page={page}";

                var response = await _httpClient.GetStringAsync(url);
                using var json = JsonDocument.Parse(response);

                JsonElement products = default;
                int pageCount = 1;

                if (json.RootElement.TryGetProperty("data", out var data))
                {
                    if (data.TryGetProperty("searchInfo", out var searchInfo))
                    {
                        if (searchInfo.TryGetProperty("storeProductInfos", out var pi))
                            products = pi;
                        if (searchInfo.TryGetProperty("pageCount", out var pc))
                            pageCount = pc.GetInt32();
                    }
                    else if (data.TryGetProperty("storeProductInfos", out var pi))
                    {
                        products = pi;
                        if (data.TryGetProperty("pageCount", out var pc))
                            pageCount = pc.GetInt32();
                    }
                }

                if (products.ValueKind != JsonValueKind.Array || products.GetArrayLength() == 0)
                {
                    break;
                }

                foreach (var p in products.EnumerateArray())
                {
                    try
                    {
                        results.Add(ParseProduct(p));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[MacroCenter] Skipped one product due to parse error: {ex.Message}");
                    }
                }

                if (page >= pageCount) break;
            }
            catch (HttpRequestException ex)
            {
                Logger.Log($"[MacroCenter] Network error for category '{targetSlug}': {ex.Message}");
                break;
            }
            catch (TaskCanceledException)
            {
                Logger.Log($"[MacroCenter] Request timed out for category '{targetSlug}'.");
                break;
            }
            catch (JsonException ex)
            {
                Logger.Log($"[MacroCenter] Failed to parse JSON for category '{targetSlug}': {ex.Message}");
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
            var url = "https://www.macrocenter.com.tr/rest/categories";
            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);

            if (json.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var topLevel in dataArray.EnumerateArray())
                {
                    ParseMigrosCategoryTree(topLevel, null, results);
                }
                _categoriesLoaded = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[MacroCenter] Failed to fetch categories: {ex.Message}");
        }

        return results;
    }

    private static string NormalizeSlug(string text)
    {
        return text.ToLowerInvariant()
            .Replace("ç", "c").Replace("ğ", "g").Replace("ı", "i")
            .Replace("ö", "o").Replace("ş", "s").Replace("ü", "u")
            .Replace(" ", "-").Replace(",", "").Replace("&", "-");
    }

    private void ParseMigrosCategoryTree(JsonElement categoryNode, string? parentPrettyName, List<PriceAggregator.Core.Data.StoreCategoryDto> results)
    {
        if (!categoryNode.TryGetProperty("data", out var data)) return;

        var name = data.TryGetProperty("name", out var n) ? n.GetString() : null;
        var prettyName = data.TryGetProperty("prettyName", out var pn) ? pn.GetString() : null;
        
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(prettyName))
        {
            _slugMap[prettyName] = prettyName;
            _slugMap[name] = prettyName;
            var clean = NormalizeSlug(name);
            _slugMap[clean] = prettyName;

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

    private ProductDto ParseProduct(JsonElement p)
    {
        string? imageUrl = null;
        if (p.TryGetProperty("images", out var images) && images.GetArrayLength() > 0 &&
            images[0].TryGetProperty("urls", out var urls) &&
            urls.TryGetProperty("PRODUCT_DETAIL", out var detailUrl))
        {
            imageUrl = detailUrl.GetString();
        }

        string? brand = null;
        if (p.TryGetProperty("brand", out var brandEl) && brandEl.TryGetProperty("name", out var brandName))
            brand = brandName.GetString();

        var (categoryName, parentName, rootName, storeCategoryId, categoryPath) = MigrosScraper.ParseCategory(p);
        var (unitType, unitAmount, sourceUnitPrice) = MigrosScraper.ParseUnitInfo(p);

        return new ProductDto
        {
            ExternalId = p.GetProperty("id").GetInt64(),
            Title = p.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Brand = brand,
            ImageUrl = imageUrl,
            Price = p.TryGetProperty("shownPrice", out var price) ? price.GetInt32() / 100m : 0,
            RegularPrice = p.TryGetProperty("regularPrice", out var regPrice) ? regPrice.GetInt32() / 100m : 0,
            ProductUrl = p.TryGetProperty("prettyName", out var pretty)
                ? $"https://www.macrocenter.com.tr/{pretty.GetString()}"
                : "",
            CategoryName = categoryName,
            StoreCategoryId = storeCategoryId,
            ParentCategoryName = parentName,
            RootCategoryName = rootName,
            CategoryPath = categoryPath,
            SourceUnitType = unitType,
            SourceUnitAmount = unitAmount,
            SourceUnitPrice = sourceUnitPrice
        };
    }

    // Same platform/shape as Migros — see MigrosScraper.FetchSpecsAsync/ParseSpecs.
    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        try
        {
            var url = $"https://www.macrocenter.com.tr/rest/products/{product.ExternalId}";
            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);
            return json.RootElement.TryGetProperty("data", out var data) ? MigrosScraper.ParseSpecs(data) : [];
        }
        catch (Exception ex) when (!MigrosScraper.IsRateLimited(ex))
        {
            Logger.Log($"[MacroCenter] Failed to fetch specs for product {product.ExternalId}: {ex.Message}");
            return [];
        }
    }
}