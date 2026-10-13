using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Scrapers
{
    public class HakmarExpressScraper : IProductScraper, IProductSpecScraper, ICategoryScraper
    {
        private readonly HttpClient _httpClient;
        private static readonly ConcurrentDictionary<int, string> _idToSlug = new();
        private static readonly SemaphoreSlim _categoryInitLock = new(1, 1);
        private static bool _categoriesLoaded;

        public string Name => "hakmarexpress";
        public ScraperSector Sector => ScraperSector.Market;

        // Gerekli HTTP basliklarini (headers) ayarlayarak HttpClient nesnesini ilklendirir.
        public HakmarExpressScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Remove("Accept");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.Remove("Accept-Language");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.8");
            _httpClient.DefaultRequestHeaders.Remove("Referer");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.hakmarexpress.com.tr/");
        }

        public async Task<List<StoreCategoryDto>> FetchStoreCategoriesAsync()
        {
            var results = new List<StoreCategoryDto>();
            try
            {
                var url = "https://api.hakmarexpress.com.tr/api/home/initialize-v2";
                var response = await _httpClient.GetStringAsync(url);
                using var json = JsonDocument.Parse(response);

                if (json.RootElement.TryGetProperty("category", out var catObj) &&
                    catObj.TryGetProperty("mainCategories", out var mainCats) &&
                    mainCats.ValueKind == JsonValueKind.Array)
                {
                    foreach (var main in mainCats.EnumerateArray())
                    {
                        TraverseCategories(main, null, _idToSlug, results);
                    }
                    _categoriesLoaded = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[HakmarExpress] Failed to fetch categories: {ex.Message}");
            }

            return results;
        }

        public async Task<List<ProductDto>> FetchProductsByCategoryAsync(string storeCategoryId)
        {
            await EnsureCategoriesLoadedAsync();
            var results = new List<ProductDto>();
            const int maxPages = 20;

            // Extract leaf slug if path or prefixed ID was provided (e.g. "hakmarexpress:kuru-gida/icecek/enerji-icecegi")
            var slug = storeCategoryId;
            if (slug.Contains(':'))
                slug = slug[(slug.IndexOf(':') + 1)..];
            if (slug.Contains('/'))
                slug = slug[(slug.LastIndexOf('/') + 1)..];

            var matchedCategory = _idToSlug.Values.FirstOrDefault(s => 
                s.Equals(slug, StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith(slug + "-", StringComparison.OrdinalIgnoreCase) ||
                slug.StartsWith(s, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchedCategory))
            {
                slug = matchedCategory;
            }

            for (int page = 1; page <= maxPages; page++)
            {
                try
                {
                    var url = $"https://api.hakmarexpress.com.tr/api/home/slug/{slug}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("x-page-number", page.ToString());

                    var response = await _httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    using var json = JsonDocument.Parse(content);

                    var pageProducts = ExtractProductsFromJson(json.RootElement, storeCategoryId);
                    if (pageProducts.Count == 0)
                    {
                        break;
                    }

                    results.AddRange(pageProducts);

                    if (json.RootElement.TryGetProperty("paging", out var pagingEl) &&
                        pagingEl.TryGetProperty("hasNextPage", out var hasNextEl) &&
                        !hasNextEl.GetBoolean())
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[HakmarExpress] Category fetch error for '{storeCategoryId}' page {page}: {ex.Message}");
                    break;
                }
            }

            return results;
        }

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

        private static void TraverseCategories(JsonElement catElement, string? parentSlug, ConcurrentDictionary<int, string> idToSlug, List<StoreCategoryDto> list)
        {
            int id = catElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            string? name = catElement.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
            string? slug = catElement.TryGetProperty("slug", out var sProp) ? sProp.GetString() : null;

            if (id > 0 && !string.IsNullOrWhiteSpace(slug))
            {
                idToSlug[id] = slug;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new StoreCategoryDto
                    {
                        StoreName = "hakmarexpress",
                        StoreCategoryId = slug,
                        StoreCategoryName = name,
                        ParentStoreCategoryId = parentSlug
                    });
                }
            }

            if (catElement.TryGetProperty("categories", out var subArray) && subArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var sub in subArray.EnumerateArray())
                {
                    TraverseCategories(sub, slug ?? parentSlug, idToSlug, list);
                }
            }
        }

        // Belirtilen arama terimine gore Hakmar Express API'sinden urunleri ceker ve dto listesi olarak doner.
        public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
        {
            await EnsureCategoriesLoadedAsync();
            var url = $"https://api.hakmarexpress.com.tr/api/home/slug/search?q={Uri.EscapeDataString(searchTerm)}";

            var response = await _httpClient.GetStringAsync(url);
            using var json = JsonDocument.Parse(response);

            return ExtractProductsFromJson(json.RootElement);
        }

        private static List<ProductDto> ExtractProductsFromJson(JsonElement rootElement, string? fallbackStoreCategoryId = null)
        {
            var results = new List<ProductDto>();
            JsonElement? productsArray = FindProductsArray(rootElement);

            if (productsArray == null)
            {
                return results;
            }

            foreach (var card in productsArray.Value.EnumerateArray())
            {
                if (card.TryGetProperty("product", out var p))
                {
                    long externalId = p.GetProperty("id").GetInt64();
                    string title = p.GetProperty("name").GetString() ?? "";
                    string? imageUrl = p.TryGetProperty("imageUrl", out var imgProp) ? imgProp.GetString() : null;

                    string? brand = p.TryGetProperty("brand", out var brandEl) && brandEl.ValueKind == JsonValueKind.Object
                        ? (brandEl.TryGetProperty("name", out var brandName) ? brandName.GetString() : null)
                        : null;

                    string? barcode = p.TryGetProperty("barcode", out var barcodeEl) && barcodeEl.ValueKind == JsonValueKind.String
                        ? barcodeEl.GetString()
                        : null;

                    decimal price = p.GetProperty("price").GetDecimal();
                    decimal regularPrice = price;

                    if (p.TryGetProperty("oldPrice", out var oldPriceProp) && oldPriceProp.ValueKind != JsonValueKind.Null)
                    {
                        regularPrice = oldPriceProp.GetDecimal();
                    }

                    string slug = p.GetProperty("slug").GetString() ?? "";
                    string productUrl = $"https://www.hakmarexpress.com.tr/{slug}";

                    var (categoryName, parentCategoryName, rootCategoryName, storeCategoryId, categoryPath) = ParseCategory(p);

                    results.Add(new ProductDto
                    {
                        ExternalId = externalId,
                        Title = title,
                        Brand = brand,
                        Barcode = barcode,
                        ImageUrl = imageUrl,
                        Price = price,
                        RegularPrice = regularPrice,
                        ProductUrl = productUrl,
                        CategoryName = categoryName,
                        ParentCategoryName = parentCategoryName,
                        RootCategoryName = rootCategoryName,
                        StoreCategoryId = storeCategoryId ?? fallbackStoreCategoryId,
                        CategoryPath = categoryPath
                    });
                }
            }

            return results;
        }

        public static (string? CategoryName, string? ParentCategoryName, string? RootCategoryName, string? StoreCategoryId, List<string> CategoryPath) ParseCategory(JsonElement p)
        {
            if (!p.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
                return (null, null, null, null, new List<string>());

            var catList = categories.EnumerateArray().ToList();
            var names = new List<string>();
            string? storeCategoryId = null;

            foreach (var c in catList)
            {
                if (c.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString()))
                {
                    names.Add(n.GetString()!.Trim());
                }
            }

            if (catList.Count > 0)
            {
                var leaf = catList[^1];
                if (leaf.TryGetProperty("slug", out var s) && !string.IsNullOrWhiteSpace(s.GetString()))
                {
                    storeCategoryId = s.GetString()!.Trim();
                }
                else if (leaf.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                {
                    int leafId = idProp.GetInt32();
                    if (_idToSlug.TryGetValue(leafId, out var mappedSlug))
                    {
                        storeCategoryId = mappedSlug;
                    }
                    else
                    {
                        storeCategoryId = leafId.ToString();
                    }
                }
            }

            if (names.Count == 0) return (null, null, null, storeCategoryId, names);
            if (names.Count == 1) return (names[0], null, null, storeCategoryId, names);
            if (names.Count == 2) return (names[1], null, names[0], storeCategoryId, names);
            return (names[^1], names[^2], names[0], storeCategoryId, names);
        }

        // Detail-page facts (nutrition/spec table, description) — see IProductSpecScraper. The
        // product-listing JSON has no id-keyed detail endpoint; the site's own page is fetched
        // by slug through the same "/api/home/slug/{slug}" component-tree endpoint the search
        // above also uses, just for a product slug instead of a search term.
        public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
        {
            var slug = ExtractSlug(product.ProductUrl);
            if (slug is null) return [];

            try
            {
                var url = $"https://api.hakmarexpress.com.tr/api/home/slug/{slug}";
                var response = await _httpClient.GetStringAsync(url);
                using var json = JsonDocument.Parse(response);

                var specs = new List<ProductSpec>();
                CollectSpecs(json.RootElement, specs);
                return specs;
            }
            catch (Exception ex) when (!MigrosScraper.IsRateLimited(ex))
            {
                Logger.Log($"[HakmarExpress] Failed to fetch specs for product {product.ExternalId}: {ex.Message}");
                return [];
            }
        }

        private const string ProductUrlPrefix = "https://www.hakmarexpress.com.tr/";

        private static string? ExtractSlug(string productUrl) =>
            productUrl.StartsWith(ProductUrlPrefix, StringComparison.OrdinalIgnoreCase)
                ? productUrl[ProductUrlPrefix.Length..]
                : null;

        // The detail page is a tree of UI "component" blocks (the same shape the site's own
        // frontend renders from). Two are worth pulling out: "table-with-sections" (the spec
        // table under the "Ürün Özellikleri" tab) and the "Ürün Açıklaması" tab's free-text
        // description. Walked generically rather than by a fixed path because the row/column
        // nesting around them varies per product.
        private static void CollectSpecs(JsonElement element, List<ProductSpec> specs)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CollectSpecs(item, specs);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object) return;

            var component = element.TryGetProperty("component", out var c) ? c.GetString() : null;

            if (component == "table-with-sections" && element.TryGetProperty("content", out var sections) && sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sections.EnumerateArray())
                {
                    var header = section.TryGetProperty("header", out var h) ? h.GetString() : null;
                    var group = string.IsNullOrWhiteSpace(header) ? "Ürün Özellikleri" : header;

                    if (!section.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array) continue;
                    foreach (var attr in attrs.EnumerateArray())
                    {
                        var key = attr.TryGetProperty("key", out var k) ? k.GetString() : null;
                        var value = attr.TryGetProperty("value", out var v) ? v.GetString() : null;
                        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;
                        specs.Add(new ProductSpec(group, key, value));
                    }
                }
            }
            else if (component == "tabs" && element.TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tab in tabs.EnumerateArray())
                {
                    var tabHeader = tab.TryGetProperty("tabHeader", out var th) ? th.GetString() : null;
                    if (tabHeader != "Ürün Açıklaması") continue;
                    if (!tab.TryGetProperty("content", out var tabContent) || tabContent.ValueKind != JsonValueKind.Array) continue;

                    foreach (var block in tabContent.EnumerateArray())
                    {
                        if (!block.TryGetProperty("component", out var bc) || bc.GetString() != "simple-text") continue;
                        var text = block.TryGetProperty("text", out var t) ? t.GetString() : null;
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        var plainText = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ").Trim();
                        if (plainText.Length > 0)
                            specs.Add(new ProductSpec("Genel Bilgiler", "Açıklama", plainText));
                    }
                }
            }

            foreach (var prop in element.EnumerateObject())
                CollectSpecs(prop.Value, specs);
        }

        // JSON agacini dolasarak 'product-list' bilesenini ve onun altindaki 'products' dizisini recursive olarak bulur.
        private static JsonElement? FindProductsArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("component", out var comp) && comp.GetString() == "product-list")
                {
                    if (element.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
                    {
                        return products;
                    }
                }

                foreach (var prop in element.EnumerateObject())
                {
                    var found = FindProductsArray(prop.Value);
                    if (found != null) return found;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindProductsArray(item);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}