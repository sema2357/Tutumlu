using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceAggregator.Core.Scrapers;

public class MigrosScraper : ICategoryScraper, IProductSpecScraper
{
    private readonly HttpClient _httpClient;

    public string Name => "migros";
    public ScraperSector Sector => ScraperSector.Market;

    public MigrosScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);

        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.migros.com.tr/");
    }

    // Migros' search response carries its own "pageCount" (live-confirmed: a broad term like
    // "su" reports 13 pages / ~380 results at 30/page) — the endpoint was only ever queried for
    // page 1, silently leaving everything past the first 30 results on the table. Capped rather
    // than walked to exhaustion to keep one broad search term from ballooning into hundreds of
    // requests; already stops early once the store's own pageCount is reached, so this only
    // matters for terms that actually have more to give.
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
                var url = $"https://www.migros.com.tr/rest/products/search?query={searchTerm}&sirala=akilli-siralama&page={page}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                if (!json.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("storeProductInfos", out var products))
                {
                    Logger.Log("[Migros] Unexpected response shape — no products found.");
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
                        Logger.Log($"[Migros] Skipped one product due to parse error: {ex.Message}");
                    }
                }

                var pageCount = data.TryGetProperty("pageCount", out var pageCountEl) ? pageCountEl.GetInt32() : 1;
                if (page >= pageCount) break;
            }
            catch (HttpRequestException ex)
            {
                Logger.Log($"[Migros] Network error: {ex.Message}");
                break;
            }
            catch (TaskCanceledException)
            {
                Logger.Log("[Migros] Request timed out.");
                break;
            }
            catch (JsonException ex)
            {
                Logger.Log($"[Migros] Failed to parse JSON: {ex.Message}");
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
        if (rawSlug.StartsWith("migros:", StringComparison.OrdinalIgnoreCase))
            rawSlug = rawSlug["migros:".Length..];

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
                    ? $"https://www.migros.com.tr/rest/search/screens/{targetSlug}?sayfa={page}"
                    : $"https://www.migros.com.tr/rest/products/search?query={Uri.EscapeDataString(targetSlug.Replace("-", " "))}&sirala=akilli-siralama&page={page}";

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
                        Logger.Log($"[Migros] Skipped one product due to parse error: {ex.Message}");
                    }
                }

                if (page >= pageCount) break;
            }
            catch (HttpRequestException ex)
            {
                Logger.Log($"[Migros] Network error for category '{targetSlug}': {ex.Message}");
                break;
            }
            catch (TaskCanceledException)
            {
                Logger.Log($"[Migros] Request timed out for category '{targetSlug}'.");
                break;
            }
            catch (JsonException ex)
            {
                Logger.Log($"[Migros] Failed to parse JSON for category '{targetSlug}': {ex.Message}");
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
            var url = "https://www.migros.com.tr/rest/categories";
            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);
            
            Console.WriteLine($"DEBUG JSON ROOT: {json.RootElement.ToString().Substring(0, Math.Min(500, json.RootElement.ToString().Length))}");

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
            Logger.Log($"[Migros] Failed to fetch categories: {ex.Message}");
            Console.WriteLine($"DEBUG: {ex.Message}");
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

        var (categoryName, parentName, rootName, storeCategoryId, categoryPath) = ParseCategory(p);
        var (unitType, unitAmount, sourceUnitPrice) = ParseUnitInfo(p);

        return new ProductDto
        {
            ExternalId = p.GetProperty("id").GetInt64(),
            Title = p.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Brand = brand,
            ImageUrl = imageUrl,
            Price = p.TryGetProperty("shownPrice", out var price) ? price.GetInt32() / 100m : 0,
            RegularPrice = p.TryGetProperty("regularPrice", out var regPrice) ? regPrice.GetInt32() / 100m : 0,
            ProductUrl = p.TryGetProperty("prettyName", out var pretty)
                ? $"https://www.migros.com.tr/{pretty.GetString()}"
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

    // Migros/MacroCenter share this JSON shape: "category" is the leaf category,
    // "categoryAscendants" lists ancestors nearest-first (last element is the root).
    public static (string? Name, string? ParentName, string? RootName, string? StoreCategoryId, List<string> Path) ParseCategory(JsonElement p)
    {
        string? categoryName = null;
        string? storeCategoryId = null;

        if (p.TryGetProperty("category", out var categoryEl))
        {
            categoryName = categoryEl.TryGetProperty("name", out var cn) ? cn.GetString() : null;
            storeCategoryId = categoryEl.TryGetProperty("prettyName", out var pn) ? pn.GetString() : null;
        }

        string? parentName = null, rootName = null;
        var path = new List<string>();

        if (p.TryGetProperty("categoryAscendants", out var ascendants) && ascendants.ValueKind == JsonValueKind.Array)
        {
            var items = ascendants.EnumerateArray().ToList();
            if (items.Count == 1)
            {
                rootName = items[0].TryGetProperty("name", out var rn) ? rn.GetString() : null;
            }
            else if (items.Count > 1)
            {
                parentName = items[0].TryGetProperty("name", out var pn) ? pn.GetString() : null;
                rootName = items[^1].TryGetProperty("name", out var rn2) ? rn2.GetString() : null;
            }

            // Ascendants are ordered nearest-first (e.g. [Sucuk, Et Şarküteri, Et, Tavuk, Balık])
            // Reverse so path is top-down (root first)
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].TryGetProperty("name", out var nEl))
                {
                    var nameStr = nEl.GetString();
                    if (!string.IsNullOrWhiteSpace(nameStr) && !path.Contains(nameStr, StringComparer.OrdinalIgnoreCase))
                        path.Add(nameStr.Trim());
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(categoryName) && !path.Contains(categoryName, StringComparer.OrdinalIgnoreCase))
        {
            path.Add(categoryName.Trim());
        }

        return (categoryName, parentName, rootName, storeCategoryId, path);
    }

    // Migros/MacroCenter/Mion run on the same commerce platform, so their product JSON
    // carries the same "unit"/"unitAmount" fields (e.g. GRAM/1000) and, for many items, a
    // ready-made legally-required "(2499,50 TL/Kg)" unitPrice label — structured size data
    // that's more reliable than guessing from the free-text title (see SizeParser).
    internal static (string? UnitType, decimal? UnitAmount, decimal? SourceUnitPrice) ParseUnitInfo(JsonElement p)
    {
        string? unitType = p.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString()
            : null;

        decimal? unitAmount = p.TryGetProperty("unitAmount", out var ua) && ua.TryGetDecimal(out var uav)
            ? uav
            : null;

        decimal? sourceUnitPrice = null;
        if (p.TryGetProperty("unitPrice", out var up) && up.ValueKind == JsonValueKind.String)
        {
            var match = Regex.Match(up.GetString() ?? "", @"[\d.,]+");
            if (match.Success)
            {
                var numeric = match.Value.Replace(".", "").Replace(",", ".");
                if (decimal.TryParse(numeric, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    sourceUnitPrice = value;
            }
        }

        return (unitType, unitAmount, sourceUnitPrice);
    }

    // Detail-page facts (nutrition, ingredients, allergens, ...) — see IProductSpecScraper.
    // Migros' own product page URL is `/rest/products/{id}`; MacroCenter and Mion run the same
    // platform on their own hosts (see MacroCenterScraper/MionScraper), which is why ParseSpecs
    // below is shared rather than duplicated per store.
    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        try
        {
            var url = $"https://www.migros.com.tr/rest/products/{product.ExternalId}";
            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);
            return json.RootElement.TryGetProperty("data", out var data) ? ParseSpecs(data) : [];
        }
        catch (Exception ex) when (!IsRateLimited(ex))
        {
            Logger.Log($"[Migros] Failed to fetch specs for product {product.ExternalId}: {ex.Message}");
            return [];
        }
    }

    // A 429 means "try again later", not "this product has no specs" — swallowing it the same
    // way as a genuine parse/shape failure would permanently mark the product as checked (see
    // MatchingService.UpsertSpecsAsync/SpecsBackfillJob, both gated by SpecsFetchedAt). Left
    // uncaught here so it propagates to those callers, which don't set the flag on a thrown
    // exception — the product stays eligible for a retry on the next backfill run.
    internal static bool IsRateLimited(Exception ex) =>
        ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests };

    // Internal logistics/registry codes with no value to a shopper — dropped by their stable
    // customId since the display name they carry isn't guaranteed stable. "foodProperty" is a
    // bare "is this food, yes/no" flag that's already implied by the product's own category.
    private static readonly HashSet<string> ExcludedSpecCustomIds = new(StringComparer.Ordinal)
    {
        "courier_weight", "productSupplierAddress", "productSupplierId", "productionPermitCertNo",
        "foodProperty"
    };

    private static readonly Dictionary<string, string> SpecGroupLabels = new(StringComparer.Ordinal)
    {
        ["MAIN"] = "Genel Bilgiler",
        ["NUTRITIONAL"] = "Beslenme Değerleri (100g/ml)",
        ["ATTRIBUTE"] = "Özellikler",
        ["LEGAL"] = "Üretici Bilgileri",
        ["OTHER"] = "Diğer",
        ["MISC"] = "Diğer",
        // "PL" (private-label) is an undocumented type seen in the wild carrying a single
        // "Sadece Migrosta" (Migros-exclusive) flag — a real fact, just filed under a raw
        // internal code with no display label of its own.
        ["PL"] = "Diğer"
    };

    // A handful of properties are booleans encoded as a bare "E"/"H" (Evet/Hayır) rather than
    // real text — e.g. "Sadece Migrosta: E". Shown as-is that reads as a typo, not a yes/no.
    private static readonly Dictionary<string, string> BooleanFlagLabels = new(StringComparer.Ordinal)
    {
        ["E"] = "Evet",
        ["H"] = "Hayır"
    };

    // Migros/MacroCenter/Mion all key detail-page facts off the same "propertyInfosMap" shape:
    // {TYPE: [{name, value, customId?}, ...]}. SEARCH_SEGMENT is the platform's own ML embedding
    // vector for search ranking (opaque floats, not a product fact), so it's the only type
    // dropped outright.
    internal static List<ProductSpec> ParseSpecs(JsonElement data)
    {
        var specs = new List<ProductSpec>();

        if (!data.TryGetProperty("propertyInfosMap", out var map) || map.ValueKind != JsonValueKind.Object)
            return specs;

        foreach (var group in map.EnumerateObject())
        {
            // SEARCH_SEGMENT is an opaque ML ranking vector. BUNDLED carries internal
            // promo/bundle linkage ("Bundled Product: 34250020|3.0" — another product id plus a
            // quantity), not a fact about this product. ENERGYLABEL's entries repeat the same
            // single-letter grade as both name and value ("A: A") with no scale/appliance
            // context attached, which reads as a data glitch rather than information.
            if (group.Name is "SEARCH_SEGMENT" or "BUNDLED" or "ENERGYLABEL" || group.Value.ValueKind != JsonValueKind.Array)
                continue;

            var label = SpecGroupLabels.TryGetValue(group.Name, out var mapped) ? mapped : group.Name;
            var groupItems = new List<ProductSpec>();

            foreach (var item in group.Value.EnumerateArray())
            {
                var customId = item.TryGetProperty("customId", out var cid) ? cid.GetString() : null;
                if (customId is not null && ExcludedSpecCustomIds.Contains(customId)) continue;

                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var value = item.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) continue;

                var displayValue = BooleanFlagLabels.TryGetValue(value, out var flagLabel) ? flagLabel : value;
                groupItems.Add(new ProductSpec(label, name, displayValue));
            }

            // A nutrition table that's zero straight down the line (e.g. bottled water) isn't
            // information — it reads the same as "no data" either way, so it's dropped instead
            // of padding the panel with a wall of zeros.
            if (group.Name == "NUTRITIONAL" && groupItems.Count > 0 && groupItems.All(s => IsZero(s.Value)))
                continue;

            specs.AddRange(groupItems);
        }

        return specs;
    }

    private static bool IsZero(string value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d == 0;
}