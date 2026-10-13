using System.Text.Json;

namespace PriceAggregator.Core.Scrapers;

/// <summary>A category node as the store itself names it: display name plus its own slug.</summary>
public record CategoryRef(string Name, string Slug);

// Browses whole category trees instead of searching one keyword at a time.
//
// MigrosScraper/MacroCenterScraper hit /rest/products/search with a keyword, which works for
// "su" or "nutella" but can't enumerate a section: cosmetics has no useful search term
// ("kozmetik" as a query matches almost nothing). Migros and MacroCenter both expose their
// category pages through a *different* endpoint —
//
//     /rest/search/screens/{categorySlug}?sayfa={n}
//
// — which returns the same product JSON, so MigrosScraper.ParseCategory/ParseUnitInfo are
// reused verbatim and every product still arrives with its real breadcrumb for
// CategoryResolver to build the tree from.
//
// Two shape differences from the search endpoint, both verified against live responses and
// both easy to get wrong:
//   1. Products sit at data.searchInfo.storeProductInfos here, not data.storeProductInfos.
//   2. `urunSayisi` is ignored — page size is fixed (30 at time of writing). So paging is
//      driven by the response's own pageCount rather than by comparing counts to a page size
//      we chose.
//
// Category slugs are NOT portable between the two stores even though they share a platform:
// the endpoint keys off the numeric suffix of the slug ("...-c-8" resolves as category-id 8),
// and the same id means different things per store. Hardcoding Migros' cosmetics slug and
// reusing it on MacroCenter silently returns an unrelated category ("Kadın Kooperatifleri",
// as it happens). Hence DiscoverRootAsync: each store's own root is found at runtime.
public class MigrosPlatformCategoryBrowser : IProductSpecScraper
{
    private const int MaxPages = 200;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _searchQueryParam;

    public string Source { get; }

    private MigrosPlatformCategoryBrowser(
        HttpClient httpClient,
        string source,
        string baseUrl,
        string searchQueryParam)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl;
        _searchQueryParam = searchQueryParam;
        Source = source;

        // Same headers the existing scrapers send. Set rather than added, because a caller
        // may hand us an HttpClient that already carries them (Add would throw on a dupe).
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        SetHeader("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        SetHeader("Accept", "application/json, text/plain, */*");
        SetHeader("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
        SetHeader("Referer", baseUrl);
    }

    private void SetHeader(string name, string value)
    {
        _httpClient.DefaultRequestHeaders.Remove(name);
        _httpClient.DefaultRequestHeaders.Add(name, value);
    }

    public static MigrosPlatformCategoryBrowser ForMigros(HttpClient httpClient) =>
        new(httpClient, "migros", "https://www.migros.com.tr/", searchQueryParam: "query");

    public static MigrosPlatformCategoryBrowser ForMacroCenter(HttpClient httpClient) =>
        new(httpClient, "macrocenter", "https://www.macrocenter.com.tr/", searchQueryParam: "q");

    /// <summary>
    /// Finds this store's own root category for a section by searching a keyword that can
    /// only live there and reading the top of the first hit's breadcrumb. Returns null if
    /// nothing is found.
    /// </summary>
    /// <remarks>
    /// Deliberately derived from a product rather than from a category-tree endpoint or a
    /// hardcoded slug: the tree endpoint's payload is large and its shape differs per store,
    /// while a slug goes stale the moment either store renumbers a category. A product's
    /// breadcrumb is authoritative by construction — it's the store telling us where it
    /// files that product right now. Migros answers "Kişisel Bakım, Kozmetik, Sağlık"
    /// (kisisel-bakim-kozmetik-saglik-c-8) and MacroCenter "Kozmetik" (kozmetik-c-117c9).
    /// </remarks>
    public async Task<CategoryRef?> DiscoverRootAsync(string probeKeyword, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}rest/products/search?{_searchQueryParam}={Uri.EscapeDataString(probeKeyword)}";

        var root = await GetJsonAsync(url, $"root discovery via '{probeKeyword}'", ct, json =>
        {
            // The search endpoint keeps products one level higher than the category endpoint.
            if (!json.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("storeProductInfos", out var products) ||
                products.ValueKind != JsonValueKind.Array ||
                products.GetArrayLength() == 0)
            {
                return null;
            }

            foreach (var p in products.EnumerateArray())
            {
                if (!p.TryGetProperty("categoryAscendants", out var ascendants) ||
                    ascendants.ValueKind != JsonValueKind.Array ||
                    ascendants.GetArrayLength() == 0)
                {
                    continue;
                }

                // Ascendants run nearest-first, so the root is the last element.
                var rootNode = ascendants[ascendants.GetArrayLength() - 1];
                var name = rootNode.TryGetProperty("name", out var n) ? n.GetString() : null;
                var slug = rootNode.TryGetProperty("prettyName", out var s) ? s.GetString() : null;

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(slug))
                    return new CategoryRef(name, slug);
            }

            return null;
        });

        if (root is null)
            Logger.Log($"[{Source}] Could not discover a root category from probe '{probeKeyword}'.");
        else
            Logger.Log($"[{Source}] Root category: '{root.Name}' ({root.Slug}).");

        return root;
    }

    /// <summary>
    /// Lists the child categories of a category, read from the CATEGORY facet the store
    /// returns alongside the products. Empty when the category is a leaf.
    /// </summary>
    /// <remarks>
    /// The facet is used rather than a category-tree endpoint because it's already in the
    /// response we need anyway, and because it only lists children that actually have
    /// products in stock right now — sweeping an empty category is wasted requests.
    /// </remarks>
    public async Task<List<CategoryRef>> DiscoverChildCategoriesAsync(string categorySlug, CancellationToken ct = default)
    {
        var children = await GetJsonAsync(CategoryUrl(categorySlug, page: 1), $"children of '{categorySlug}'", ct, json =>
        {
            var found = new List<CategoryRef>();

            if (!json.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("searchInfo", out var searchInfo) ||
                !searchInfo.TryGetProperty("aggregationGroups", out var groups) ||
                groups.ValueKind != JsonValueKind.Array)
            {
                return found;
            }

            foreach (var group in groups.EnumerateArray())
            {
                // Groups also cover BRAND, DISCOUNT and PROPERTY facets; only CATEGORY is a
                // subtree.
                var type = group.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type != "CATEGORY") continue;

                if (!group.TryGetProperty("aggregationInfos", out var infos) ||
                    infos.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var info in infos.EnumerateArray())
                {
                    var name = info.TryGetProperty("label", out var l) ? l.GetString() : null;
                    var slug = info.TryGetProperty("prettyName", out var s) ? s.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(slug))
                        found.Add(new CategoryRef(name, slug));
                }
            }

            return found;
        }) ?? [];

        Logger.Log($"[{Source}] '{categorySlug}': {children.Count} child categories.");
        return children;
    }

    /// <summary>
    /// Fetches every product in one category, following pagination to the end. Returns an
    /// empty list — never throws — when the category is empty or a request fails.
    /// </summary>
    public async Task<List<ProductDto>> FetchCategoryAsync(string categorySlug, CancellationToken ct = default)
    {
        var results = new List<ProductDto>();

        // These endpoints sometimes re-serve a page rather than returning an empty one past
        // the end. Without this guard that becomes a page's worth of duplicates on every
        // extra iteration — all MERGEing onto the same SellerProducts row, so the cost is
        // pure wasted DB round-trips.
        var seenIds = new HashSet<long>();
        int pageCount = 1;

        for (int page = 1; page <= Math.Min(pageCount, MaxPages); page++)
        {
            if (ct.IsCancellationRequested) break;

            var pageItems = await GetJsonAsync(CategoryUrl(categorySlug, page), $"'{categorySlug}' page {page}", ct, json =>
            {
                if (!json.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("searchInfo", out var searchInfo) ||
                    !searchInfo.TryGetProperty("storeProductInfos", out var products) ||
                    products.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                // Total pages is only known once the store tells us, and it can shrink as
                // stock changes mid-sweep, so it's re-read on every page rather than trusted
                // from page 1.
                if (searchInfo.TryGetProperty("pageCount", out var pc) && pc.TryGetInt32(out var pcValue))
                    pageCount = pcValue;

                return ParsePage(products, seenIds);
            });

            if (pageItems is null)
            {
                if (page == 1)
                    Logger.Log($"[{Source}] No products under '{categorySlug}' — slug may not exist on this store.");
                break;
            }

            results.AddRange(pageItems);

            // Every product on the page was already seen: we're being served a repeat, so
            // there's nothing further to walk.
            if (pageItems.Count == 0) break;

            // Deliberately unhurried. A full cosmetics sweep is dozens of categories across
            // two stores on undocumented endpoints the stores never agreed to serve us; half
            // a second between pages keeps it closer to browsing than to hammering.
            //
            // Cancellation is swallowed rather than propagated so this method keeps its
            // "returns what it got, never throws" contract; the loop condition above sees the
            // token on the next pass and stops.
            if (page < pageCount)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        Logger.Log($"[{Source}] '{categorySlug}': {results.Count} products.");
        return results;
    }

    // Same /rest/products/{id} detail endpoint and propertyInfosMap shape as
    // MigrosScraper/MacroCenterScraper — used here for the Kozmetik sweep (ingredients, usage,
    // skin/hair type, ...) since CosmeticsScrapeJob drives products through this browser rather
    // than through those scrapers directly.
    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        try
        {
            var url = $"{_baseUrl}rest/products/{product.ExternalId}";
            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);
            return json.RootElement.TryGetProperty("data", out var data) ? MigrosScraper.ParseSpecs(data) : [];
        }
        catch (Exception ex) when (!MigrosScraper.IsRateLimited(ex))
        {
            Logger.Log($"[{Source}] Failed to fetch specs for product {product.ExternalId}: {ex.Message}");
            return [];
        }
    }

    private string CategoryUrl(string categorySlug, int page) =>
        $"{_baseUrl}rest/search/screens/{Uri.EscapeDataString(categorySlug)}?sayfa={page}";

    // One place for the HTTP + JSON error handling all three public methods need, so a
    // network blip or a shape change degrades to "returns nothing, logged" everywhere
    // instead of throwing out of a half-finished sweep.
    private async Task<T?> GetJsonAsync<T>(string url, string what, CancellationToken ct, Func<JsonElement, T?> parse)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url, ct);
            using var json = JsonDocument.Parse(response);
            return parse(json.RootElement);
        }
        catch (HttpRequestException ex)
        {
            Logger.Log($"[{Source}] Network error fetching {what}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            // HttpClient surfaces both a timeout and a cancelled token as this exception.
            // Only the timeout is worth a log line — a cancelled sweep isn't a fault, and
            // neither is rethrown, so callers keep the partial results they already have.
            if (!ct.IsCancellationRequested)
                Logger.Log($"[{Source}] Request timed out fetching {what}.");
        }
        catch (JsonException ex)
        {
            Logger.Log($"[{Source}] Failed to parse JSON for {what}: {ex.Message}");
        }

        return default;
    }

    private List<ProductDto> ParsePage(JsonElement products, HashSet<long> seenIds)
    {
        var pageItems = new List<ProductDto>();

        foreach (var p in products.EnumerateArray())
        {
            try
            {
                var product = ParseProduct(p, _baseUrl);
                if (!seenIds.Add(product.ExternalId)) continue;
                pageItems.Add(product);
            }
            catch (Exception ex)
            {
                Logger.Log($"[{Source}] Skipped one product due to parse error: {ex.Message}");
            }
        }

        return pageItems;
    }

    // Same field mapping as MigrosScraper.ParseProduct / MacroCenterScraper.ParseProduct,
    // with the product URL host as a parameter since both stores share this JSON shape.
    internal static ProductDto ParseProduct(JsonElement p, string baseUrl)
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

        var (categoryName, parentName, rootName, _, categoryPath) = MigrosScraper.ParseCategory(p);
        var (unitType, unitAmount, sourceUnitPrice) = MigrosScraper.ParseUnitInfo(p);

        return new ProductDto
        {
            ExternalId = p.GetProperty("id").GetInt64(),
            Title = p.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Brand = brand ?? "",
            ImageUrl = imageUrl,
            Price = p.TryGetProperty("shownPrice", out var price) ? price.GetInt32() / 100m : 0,
            RegularPrice = p.TryGetProperty("regularPrice", out var regPrice) ? regPrice.GetInt32() / 100m : 0,
            ProductUrl = p.TryGetProperty("prettyName", out var pretty)
                ? baseUrl + pretty.GetString()
                : "",
            CategoryName = categoryName,
            ParentCategoryName = parentName,
            RootCategoryName = rootName,
            CategoryPath = categoryPath,
            SourceUnitType = unitType,
            SourceUnitAmount = unitAmount,
            SourceUnitPrice = sourceUnitPrice
        };
    }
}
