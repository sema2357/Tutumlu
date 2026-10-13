using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Scrapers;

public class TeknosaScraper : IProductScraper, IProductSpecScraper, IAsyncDisposable
{
    public string Name => "teknosa";
    public ScraperSector Sector => ScraperSector.Mobilya;

    private readonly HttpClient? _httpClient;
    private const int DefaultMaxPages = 5;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private readonly SemaphoreSlim _browserLock = new(1, 1);

    public TeknosaScraper()
    {
    }

    public TeknosaScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private async Task EnsureBrowserAsync()
    {
        if (_browser != null) return;
        await _browserLock.WaitAsync();
        try
        {
            if (_browser == null)
            {
                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--no-sandbox"
                    }
                });
            }
        }
        finally
        {
            _browserLock.Release();
        }
    }

    /// <summary>
    /// Fetches live HTML of a page using a fresh Playwright browser context per request.
    /// Returns null if navigation fails or returns non-2xx status (e.g. 403 Forbidden).
    /// </summary>
    public async Task<string?> FetchHtmlAsync(string url, string? referer = null)
    {
        IBrowserContext? context = null;
        IPage? page = null;
        try
        {
            await EnsureBrowserAsync();
            context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "tr-TR"
            });
            page = await context.NewPageAsync();

            var options = new PageGotoOptions
            {
                Timeout = 30000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            };

            if (!string.IsNullOrWhiteSpace(referer))
            {
                options.Referer = referer;
            }

            var response = await page.GotoAsync(url, options);

            if (response == null || !response.Ok)
            {
                var statusCode = response?.Status;
                if (statusCode == 403)
                {
                    Logger.Log($"[TeknosaScraper] Teknosa request failed: HTTP 403 Forbidden. No live product data was retrieved for '{url}'");
                }
                else
                {
                    Logger.Log($"[TeknosaScraper] Teknosa request failed: HTTP {statusCode}. No live product data was retrieved for '{url}'");
                }
                return null;
            }

            return await page.ContentAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"[TeknosaScraper] Browser navigation failed for '{url}': {ex.Message}");
            return null;
        }
        finally
        {
            if (page != null)
            {
                await page.CloseAsync();
            }
            if (context != null)
            {
                await context.CloseAsync();
            }
        }
    }

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm) ||
            searchTerm.Equals("ALL_CATEGORIES", StringComparison.OrdinalIgnoreCase) ||
            searchTerm.Equals("teknosa_catalog", StringComparison.OrdinalIgnoreCase))
        {
            return await FetchCategoryProductsAsync(maxCategories: 150, maxProductsPerCategory: 1);
        }
        return await FetchProductsAsync(searchTerm, DefaultMaxPages);
    }

    /// <summary>
    /// Fetches catalog/search pages using Playwright and extracts products directly
    /// from the search listing page HTML (window.insider_object / data-product attributes).
    /// Eliminates unnecessary PDP requests for core product ingestion.
    /// </summary>
    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm, int maxPages)
    {
        var results = new List<ProductDto>();
        if (string.IsNullOrWhiteSpace(searchTerm)) return results;

        var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedSkus = new HashSet<long>();

        for (int pageIndex = 1; pageIndex <= maxPages; pageIndex++)
        {
            var listingUrl = BuildListingUrl(searchTerm, pageIndex);
            string? html = await FetchHtmlAsync(listingUrl);

            if (string.IsNullOrWhiteSpace(html))
            {
                Logger.Log($"[TeknosaScraper] Listing page fetch failed for '{searchTerm}' at page {pageIndex}. Ending search iteration.");
                break;
            }

            var pageProducts = ExtractProductsFromListingHtml(html, listingUrl);
            if (pageProducts.Count == 0)
            {
                Logger.Log($"[TeknosaScraper] No products extracted on listing page {pageIndex} for '{searchTerm}'. Stopping pagination.");
                break;
            }

            int newProductsOnPage = 0;
            foreach (var p in pageProducts)
            {
                if (p.ExternalId != 0 && !processedSkus.Add(p.ExternalId)) continue;
                if (!string.IsNullOrWhiteSpace(p.ProductUrl) && !processedUrls.Add(p.ProductUrl)) continue;

                results.Add(p);
                newProductsOnPage++;
            }

            if (newProductsOnPage == 0)
            {
                Logger.Log($"[TeknosaScraper] No new products added on page {pageIndex}. Ending iteration.");
                break;
            }
        }

        return results;
    }

    public record CategoryInfo(string Name, string Url, string CategoryId);

    /// <summary>
    /// Discovers live Teknosa lowest subcategories (leaf categories) from the homepage/navigation HTML.
    /// </summary>
    public async Task<List<CategoryInfo>> DiscoverCategoriesAsync()
    {
        var leafCategories = new List<CategoryInfo>();
        var pageHtml = await FetchHtmlAsync("https://www.teknosa.com/");

        if (string.IsNullOrWhiteSpace(pageHtml))
        {
            Logger.Log("[TeknosaScraper] Failed to fetch homepage for category discovery.");
            return leafCategories;
        }

        var matches = Regex.Matches(pageHtml, @"<a[^>]*href=""([^""]*?-c-(\d+))""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var categoryMap = new Dictionary<string, CategoryInfo>();

        foreach (Match match in matches)
        {
            var rawUrl = match.Groups[1].Value.Trim();
            var catIdStr = match.Groups[2].Value.Trim();
            var rawName = StripHtml(match.Groups[3].Value).Trim();

            if (string.IsNullOrWhiteSpace(rawUrl) || string.IsNullOrWhiteSpace(catIdStr)) continue;

            string fullUrl = rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rawUrl
                : "https://www.teknosa.com" + (rawUrl.StartsWith("/") ? rawUrl : "/" + rawUrl);

            if (fullUrl.Contains("?"))
            {
                fullUrl = fullUrl.Split('?')[0];
            }

            if (string.IsNullOrWhiteSpace(rawName))
            {
                var slugMatch = Regex.Match(fullUrl, @"teknosa\.com/([a-z0-9-]+)-c-\d+");
                if (slugMatch.Success) rawName = slugMatch.Groups[1].Value.Replace("-", " ");
            }

            if (string.IsNullOrWhiteSpace(rawName) || rawName.Length < 2) continue;
            rawName = Regex.Replace(System.Net.WebUtility.HtmlDecode(rawName), @"\s+", " ").Trim();

            if (rawName.Equals("Anasayfa", StringComparison.OrdinalIgnoreCase) || rawName.Equals("Tüm Kategoriler", StringComparison.OrdinalIgnoreCase)) continue;

            if (!categoryMap.ContainsKey(catIdStr))
            {
                categoryMap[catIdStr] = new CategoryInfo(rawName, fullUrl, catIdStr);
            }
        }

        // Identify lowest/leaf categories:
        // A category is a leaf subcategory if no other category ID in the catalog starts with its ID as a prefix (it has no sub-children) or if its catId length is >= 9 digits.
        var allCatIds = categoryMap.Keys.ToList();

        foreach (var kvp in categoryMap)
        {
            var catId = kvp.Key;
            var catInfo = kvp.Value;

            bool hasChildren = allCatIds.Any(otherId => otherId != catId && otherId.StartsWith(catId));

            if (!hasChildren || catId.Length >= 9)
            {
                leafCategories.Add(catInfo);
            }
        }

        return leafCategories;
    }

    /// <summary>
    /// Traverses discovered categories and extracts all unique valid products across pages with robust safeguards and category timeouts.
    /// </summary>
    public async Task<List<ProductDto>> FetchCategoryProductsAsync(
        int maxCategories = int.MaxValue,
        int maxProductsPerCategory = int.MaxValue,
        int maxPagesPerCategory = 100,
        int categoryTimeoutSeconds = 180,
        List<CategoryInfo>? specificCategories = null)
    {
        var results = new List<ProductDto>();
        var discoveredCategories = specificCategories ?? await DiscoverCategoriesAsync();

        Console.WriteLine($"\nDiscovered/Targeted categories: {discoveredCategories.Count}");
        if (discoveredCategories.Count == 0)
        {
            Logger.Log("[TeknosaScraper] No categories to scrape.");
            return results;
        }

        int processedCount = 0;
        int successCount = 0;
        int emptyCount = 0;
        int failedCount = 0;

        var processedSkus = new HashSet<long>();
        var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var targetCategories = discoveredCategories.Take(maxCategories).ToList();

        for (int i = 0; i < targetCategories.Count; i++)
        {
            var category = targetCategories[i];
            processedCount++;

            Console.WriteLine($"\n[{processedCount}/{targetCategories.Count}] {category.Name}");
            Console.WriteLine($"    URL: {category.Url}");

            int extractedForThisCategory = 0;
            int pageIndex = 1;
            int consecutiveEmptyPages = 0;
            var visitedCategoryUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var categoryCts = new CancellationTokenSource(TimeSpan.FromSeconds(categoryTimeoutSeconds));

            try
            {
                while (extractedForThisCategory < maxProductsPerCategory && pageIndex <= maxPagesPerCategory)
                {
                    categoryCts.Token.ThrowIfCancellationRequested();

                    var categoryPageUrl = BuildListingUrl(category.Url, pageIndex);
                    if (!visitedCategoryUrls.Add(categoryPageUrl))
                    {
                        Logger.Log($"    -> [TeknosaScraper] Duplicate page URL detected: '{categoryPageUrl}'. Stopping category pagination.");
                        break;
                    }

                    string? html = await FetchHtmlAsync(categoryPageUrl, referer: "https://www.teknosa.com/");

                    if (string.IsNullOrWhiteSpace(html))
                    {
                        if (pageIndex == 1)
                        {
                            Console.WriteLine($"    → FAILED (HTTP or navigation error)");
                            failedCount++;
                        }
                        break;
                    }

                    var categoryProducts = ExtractProductsFromListingHtml(html, categoryPageUrl);
                    if (categoryProducts.Count == 0)
                    {
                        if (pageIndex == 1)
                        {
                            Console.WriteLine($"    → NO PRODUCTS (Listing page returned 0 products)");
                            emptyCount++;
                        }
                        break;
                    }

                    int newOnPage = 0;
                    foreach (var p in categoryProducts)
                    {
                        if (extractedForThisCategory >= maxProductsPerCategory) break;

                        if (p.ExternalId != 0 && !processedSkus.Add(p.ExternalId)) continue;
                        if (!string.IsNullOrWhiteSpace(p.ProductUrl) && !processedUrls.Add(p.ProductUrl)) continue;

                        results.Add(p);
                        extractedForThisCategory++;
                        newOnPage++;
                        Console.WriteLine($"    → {p.Title} | Price: {p.Price:N2} TRY | Store: {p.StoreName} | Cat: {p.RootCategoryName} > {p.ParentCategoryName} > {p.CategoryName}");
                    }

                    if (newOnPage == 0)
                    {
                        consecutiveEmptyPages++;
                        if (consecutiveEmptyPages >= 2)
                        {
                            Logger.Log($"    -> [TeknosaScraper] 2 consecutive pages yielded no new items for '{category.Name}'. Stopping pagination.");
                            break;
                        }
                    }
                    else
                    {
                        consecutiveEmptyPages = 0;
                    }

                    pageIndex++;
                    await Task.Delay(200, categoryCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"    -> [TeknosaScraper] Category timeout ({categoryTimeoutSeconds}s) reached for '{category.Name}'. Proceeding to next category.");
                failedCount++;
                continue;
            }
            catch (Exception ex)
            {
                Logger.Log($"    -> [TeknosaScraper] Error during category scrape for '{category.Name}': {ex.Message}");
                failedCount++;
                continue;
            }

            if (extractedForThisCategory > 0)
            {
                successCount++;
            }
            else if (failedCount < processedCount && emptyCount < processedCount)
            {
                Console.WriteLine($"    → DUPLICATES ONLY (All products on page were duplicates of previously extracted items)");
                emptyCount++;
            }
        }

        Console.WriteLine($"\n========================================");
        Console.WriteLine($"Categories processed: {processedCount}");
        Console.WriteLine($"Categories with products: {successCount}");
        Console.WriteLine($"Categories without products: {emptyCount}");
        Console.WriteLine($"Categories failed: {failedCount}");
        Console.WriteLine($"Products extracted: {results.Count}");
        Console.WriteLine($"========================================\n");

        return results;
    }

    /// <summary>
    /// Extracts structured product records directly from search/category listing page HTML.
    /// </summary>
    public static List<ProductDto> ExtractProductsFromListingHtml(string html, string pageUrl)
    {
        var products = new List<ProductDto>();
        if (string.IsNullOrWhiteSpace(html)) return products;

        // Primary: Parse window.insider_object.listing.items from listing JS
        products = ParseProductsFromInsiderListingJs(html);
        if (products.Count > 0) return products;

        // Backup: Parse data-product-* attributes on product cards in HTML
        products = ParseProductsFromDataAttributesHtml(html);
        return products;
    }

    private static List<ProductDto> ParseProductsFromInsiderListingJs(string html)
    {
        var products = new List<ProductDto>();
        var match = Regex.Match(html, @"window\.insider_object\s*=\s*(\{.*?\});", RegexOptions.Singleline);
        if (!match.Success) return products;

        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            var root = doc.RootElement;

            JsonElement itemsEl = default;
            if (root.TryGetProperty("listing", out var listingEl) && listingEl.TryGetProperty("items", out var listItemsEl) && listItemsEl.ValueKind == JsonValueKind.Array)
            {
                itemsEl = listItemsEl;
            }
            else if (root.TryGetProperty("category", out var catEl) && catEl.TryGetProperty("items", out var catItemsEl) && catItemsEl.ValueKind == JsonValueKind.Array)
            {
                itemsEl = catItemsEl;
            }
            else if (root.TryGetProperty("items", out var rootItemsEl) && rootItemsEl.ValueKind == JsonValueKind.Array)
            {
                itemsEl = rootItemsEl;
            }

            if (itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsEl.EnumerateArray())
                {
                    var dto = new ProductDto();

                    if (item.TryGetProperty("name", out var nameEl))
                        dto.Title = nameEl.GetString()?.Trim() ?? "";

                    if (item.TryGetProperty("id", out var idEl))
                    {
                        var idStr = idEl.GetString() ?? "";
                        var matchId = Regex.Match(idStr, @"^\d+");
                        if (matchId.Success && long.TryParse(matchId.Value, out long parsedId))
                            dto.ExternalId = parsedId;
                    }

                    if (item.TryGetProperty("unit_sale_price", out var salePriceEl))
                        dto.Price = ParsePriceElement(salePriceEl);

                    if (item.TryGetProperty("unit_price", out var unitPriceEl))
                        dto.RegularPrice = ParsePriceElement(unitPriceEl);

                    if (dto.Price == 0 && dto.RegularPrice > 0) dto.Price = dto.RegularPrice;
                    if (dto.RegularPrice < dto.Price) dto.RegularPrice = dto.Price;

                    if (item.TryGetProperty("url", out var urlEl))
                    {
                        var rawUrl = urlEl.GetString() ?? "";
                        dto.ProductUrl = rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? rawUrl
                            : "https://www.teknosa.com" + (rawUrl.StartsWith("/") ? rawUrl : "/" + rawUrl);
                    }

                    if (item.TryGetProperty("product_image_url", out var imgEl))
                        dto.ImageUrl = imgEl.GetString();

                    if (item.TryGetProperty("taxonomy", out var taxEl) && taxEl.ValueKind == JsonValueKind.Array)
                    {
                        var cats = taxEl.EnumerateArray()
                            .Select(x => x.GetString()?.Trim())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x!)
                            .ToList();

                        if (cats.Count == 1)
                        {
                            dto.RootCategoryName = cats[0];
                            dto.CategoryName = cats[0];
                        }
                        else if (cats.Count == 2)
                        {
                            dto.RootCategoryName = cats[0];
                            dto.ParentCategoryName = cats[0];
                            dto.CategoryName = cats[1];
                        }
                        else if (cats.Count >= 3)
                        {
                            dto.RootCategoryName = cats[0];
                            dto.ParentCategoryName = cats[1];
                            dto.CategoryName = cats[cats.Count - 1];
                        }

                        dto.CategoryPath = cats;
                    }

                    if (item.TryGetProperty("custom", out var customEl) && customEl.TryGetProperty("shop_name", out var shopEl))
                    {
                        var shop = shopEl.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(shop))
                            dto.StoreName = shop;
                    }
                    if (string.IsNullOrWhiteSpace(dto.StoreName))
                    {
                        dto.StoreName = "TEKNOSA";
                    }

                    if (item.TryGetProperty("brand", out var brandEl))
                    {
                        dto.Brand = brandEl.GetString()?.Trim() ?? "";
                    }
                    if (string.IsNullOrWhiteSpace(dto.Brand) && !string.IsNullOrWhiteSpace(dto.Title))
                    {
                        dto.Brand = dto.Title.Split(' ')[0];
                    }

                    if (!string.IsNullOrWhiteSpace(dto.Title) && dto.ExternalId != 0 && dto.Price > 0)
                    {
                        products.Add(dto);
                    }
                }
            }
        }
        catch
        {
            // Ignore parse failures and fall back to HTML data attributes
        }

        return products;
    }

    private static List<ProductDto> ParseProductsFromDataAttributesHtml(string html)
    {
        var products = new List<ProductDto>();
        var cardMatches = Regex.Matches(html, @"<div[^>]*class=""[^""]*prd[^""]*""(.*?)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match card in cardMatches)
        {
            var attrs = card.Groups[1].Value;
            var dto = new ProductDto();

            var nameMatch = Regex.Match(attrs, @"data-product-name=""([^""]+)""");
            if (nameMatch.Success) dto.Title = System.Net.WebUtility.HtmlDecode(nameMatch.Groups[1].Value).Trim();

            var idMatch = Regex.Match(attrs, @"data-product-id=""([^""]+)""");
            if (idMatch.Success && long.TryParse(idMatch.Groups[1].Value, out long extId)) dto.ExternalId = extId;

            var brandMatch = Regex.Match(attrs, @"data-product-brand=""([^""]+)""");
            if (brandMatch.Success) dto.Brand = System.Net.WebUtility.HtmlDecode(brandMatch.Groups[1].Value).Trim();

            var priceMatch = Regex.Match(attrs, @"data-product-discounted-price=""([^""]+)""");
            if (!priceMatch.Success) priceMatch = Regex.Match(attrs, @"data-product-price=""([^""]+)""");
            if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
            {
                dto.Price = price;
                dto.RegularPrice = price;
            }

            var listPriceMatch = Regex.Match(attrs, @"data-product-price=""([^""]+)""");
            if (listPriceMatch.Success && decimal.TryParse(listPriceMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal regPrice))
            {
                dto.RegularPrice = regPrice;
                if (dto.RegularPrice < dto.Price) dto.RegularPrice = dto.Price;
            }

            var urlMatch = Regex.Match(attrs, @"data-product-url=""([^""]+)""");
            if (urlMatch.Success)
            {
                var rawUrl = urlMatch.Groups[1].Value.Trim();
                dto.ProductUrl = rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? rawUrl
                    : "https://www.teknosa.com" + (rawUrl.StartsWith("/") ? rawUrl : "/" + rawUrl);
            }

            var storeMatch = Regex.Match(attrs, @"data-shop-name=""([^""]+)""");
            if (storeMatch.Success) dto.StoreName = storeMatch.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(dto.StoreName)) dto.StoreName = "TEKNOSA";

            var catStructureMatch = Regex.Match(attrs, @"data-product-category-structure=""([^""]+)""");
            if (catStructureMatch.Success)
            {
                var parts = System.Net.WebUtility.HtmlDecode(catStructureMatch.Groups[1].Value)
                    .Split('>')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p) && !p.Equals("Kategoriler", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (parts.Count > 0) dto.RootCategoryName = parts[0];
                if (parts.Count > 1) dto.ParentCategoryName = parts[1];
                if (parts.Count > 2) dto.CategoryName = parts[parts.Count - 1];
                dto.CategoryPath = parts;
            }

            if (!string.IsNullOrWhiteSpace(dto.Title) && dto.ExternalId != 0 && dto.Price > 0)
            {
                products.Add(dto);
            }
        }

        return products;
    }

    public async Task<ProductDto?> FetchProductDetailAsync(string productUrl, string? referer = null)
    {
        var html = await FetchHtmlAsync(productUrl, referer);
        if (string.IsNullOrWhiteSpace(html)) return null;

        var product = ParseProductDetailFromHtml(html, productUrl);
        if (product != null) return product;

        Logger.Log($"[TeknosaScraper] Failed to extract product data from URL: {productUrl}");
        return null;
    }

    public ProductDto? ParseProductDetailFromHtml(string html, string productUrl)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var product = ParseProductFromJsonLd(html, productUrl);
        product ??= ParseProductFromInsiderObject(html, productUrl);
        product ??= ParseProductFromDataLayerOrMeta(html, productUrl);

        if (product != null)
        {
            if (string.IsNullOrWhiteSpace(product.CategoryName) || string.IsNullOrWhiteSpace(product.RootCategoryName))
            {
                var htmlBreadcrumbs = HtmlBreadcrumbExtractor.Extract(html);
                if (htmlBreadcrumbs.Count > 0)
                {
                    var (root, parent, leaf) = HtmlBreadcrumbExtractor.ToHierarchy(htmlBreadcrumbs);
                    if (!string.IsNullOrWhiteSpace(root)) product.RootCategoryName = root;
                    if (!string.IsNullOrWhiteSpace(parent)) product.ParentCategoryName = parent;
                    if (!string.IsNullOrWhiteSpace(leaf)) product.CategoryName = leaf;
                    product.CategoryPath = htmlBreadcrumbs;
                }
            }
        }

        return product;
    }

    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        var specs = new List<ProductSpec>();
        if (string.IsNullOrWhiteSpace(product.ProductUrl)) return specs;

        try
        {
            var html = await FetchHtmlAsync(product.ProductUrl);
            if (string.IsNullOrWhiteSpace(html)) return specs;

            var tableMatches = Regex.Matches(html, @"<tr[^>]*>\s*<th[^>]*>(.*?)<\/th>\s*<td[^>]*>(.*?)<\/td>\s*<\/tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match match in tableMatches)
            {
                var name = StripHtml(match.Groups[1].Value).Trim();
                var val = StripHtml(match.Groups[2].Value).Trim();

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(val))
                {
                    specs.Add(new ProductSpec("Teknik Özellikler", name, val));
                }
            }

            if (specs.Count == 0)
            {
                var dtDdMatches = Regex.Matches(html, @"<(?:dt|div)[^>]*class=""[^""]*spec-name[^""]*""[^>]*>(.*?)<\/(?:dt|div)>\s*<(?:dd|div)[^>]*class=""[^""]*spec-value[^""]*""[^>]*>(.*?)<\/(?:dd|div)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match match in dtDdMatches)
                {
                    var name = StripHtml(match.Groups[1].Value).Trim();
                    var val = StripHtml(match.Groups[2].Value).Trim();

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(val))
                    {
                        specs.Add(new ProductSpec("Teknik Özellikler", name, val));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[TeknosaScraper] Error fetching specs for '{product.Title}': {ex.Message}");
        }

        return specs;
    }

    private static string BuildListingUrl(string target, int pageIndex)
    {
        if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var separator = target.Contains("?") ? "&" : "?";
            if (target.Contains("page="))
            {
                return Regex.Replace(target, @"page=\d+", $"page={pageIndex}");
            }
            return $"{target}{separator}page={pageIndex}";
        }

        return $"https://www.teknosa.com/arama/?s={Uri.EscapeDataString(target)}&page={pageIndex}";
    }

    private static ProductDto? ParseProductFromJsonLd(string html, string pageUrl)
    {
        var scriptMatches = Regex.Matches(html, @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match scriptMatch in scriptMatches)
        {
            var jsonText = scriptMatch.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(jsonText)) continue;

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                JsonElement productEl = default;
                JsonElement breadcrumbEl = default;

                if (root.TryGetProperty("@graph", out var graphEl))
                {
                    if (graphEl.ValueKind == JsonValueKind.Object && graphEl.TryGetProperty("@type", out var gType) && gType.GetString() == "Product")
                    {
                        productEl = graphEl;
                    }
                    else if (graphEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in graphEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("@type", out var itemType) && itemType.GetString() == "Product")
                            {
                                productEl = item;
                                break;
                            }
                        }
                    }
                }

                if (productEl.ValueKind == JsonValueKind.Undefined && root.TryGetProperty("@type", out var rType) && rType.GetString() == "Product")
                {
                    productEl = root;
                }

                if (root.TryGetProperty("breadcrumb", out var bc))
                {
                    breadcrumbEl = bc;
                }

                if (productEl.ValueKind != JsonValueKind.Undefined)
                {
                    var dto = new ProductDto();
                    dto.ProductUrl = pageUrl;

                    if (productEl.TryGetProperty("name", out var nameEl))
                        dto.Title = nameEl.GetString() ?? "";

                    if (productEl.TryGetProperty("sku", out var skuEl))
                    {
                        var skuStr = skuEl.GetString() ?? "";
                        if (long.TryParse(skuStr, out long parsedId))
                            dto.ExternalId = parsedId;
                        else if (long.TryParse(Regex.Match(skuStr, @"\d+").Value, out long regexId))
                            dto.ExternalId = regexId;
                    }

                    if (dto.ExternalId == 0)
                    {
                        var urlIdMatch = Regex.Match(pageUrl, @"-p-(\d+)");
                        if (urlIdMatch.Success && long.TryParse(urlIdMatch.Groups[1].Value, out long urlId))
                            dto.ExternalId = urlId;
                    }

                    if (productEl.TryGetProperty("brand", out var brandEl))
                    {
                        if (brandEl.ValueKind == JsonValueKind.Object && brandEl.TryGetProperty("name", out var brandNameEl))
                            dto.Brand = brandNameEl.GetString() ?? "";
                        else if (brandEl.ValueKind == JsonValueKind.String)
                            dto.Brand = brandEl.GetString() ?? "";
                    }

                    if (productEl.TryGetProperty("image", out var imageEl))
                    {
                        if (imageEl.ValueKind == JsonValueKind.Array && imageEl.GetArrayLength() > 0)
                            dto.ImageUrl = imageEl[0].GetString();
                        else if (imageEl.ValueKind == JsonValueKind.String)
                            dto.ImageUrl = imageEl.GetString();
                    }

                    if (productEl.TryGetProperty("offers", out var offersEl))
                    {
                        JsonElement offerObj = default;
                        if (offersEl.ValueKind == JsonValueKind.Object)
                            offerObj = offersEl;
                        else if (offersEl.ValueKind == JsonValueKind.Array && offersEl.GetArrayLength() > 0)
                            offerObj = offersEl[0];

                        if (offerObj.ValueKind != JsonValueKind.Undefined)
                        {
                            if (offerObj.TryGetProperty("price", out var priceEl))
                            {
                                dto.Price = ParsePriceElement(priceEl);
                                dto.RegularPrice = dto.Price;
                            }

                            if (offerObj.TryGetProperty("seller", out var sellerEl))
                            {
                                if (sellerEl.ValueKind == JsonValueKind.Object && sellerEl.TryGetProperty("name", out var sellerNameEl))
                                    dto.StoreName = sellerNameEl.GetString();
                                else if (sellerEl.ValueKind == JsonValueKind.String)
                                    dto.StoreName = sellerEl.GetString();
                            }
                        }
                    }

                    if (breadcrumbEl.ValueKind == JsonValueKind.Object && breadcrumbEl.TryGetProperty("itemListElement", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        var categoryList = new List<string>();
                        foreach (var item in itemsEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("name", out var catNameEl))
                            {
                                var catName = catNameEl.GetString()?.Trim();
                                if (!string.IsNullOrWhiteSpace(catName) &&
                                    !catName.Equals("Anasayfa", StringComparison.OrdinalIgnoreCase) &&
                                    !catName.Equals("Ana Sayfa", StringComparison.OrdinalIgnoreCase))
                                {
                                    categoryList.Add(catName);
                                }
                            }
                        }

                        if (categoryList.Count > 0) dto.RootCategoryName = categoryList[0];
                        if (categoryList.Count > 1) dto.ParentCategoryName = categoryList[1];
                        if (categoryList.Count > 2) dto.CategoryName = categoryList[categoryList.Count - 1];
                    }

                    if (!string.IsNullOrWhiteSpace(dto.Title) && dto.ExternalId != 0 && dto.Price > 0)
                    {
                        return dto;
                    }
                }
            }
            catch
            {
                // Ignore JSON parse errors
            }
        }

        return null;
    }

    private static ProductDto? ParseProductFromInsiderObject(string html, string pageUrl)
    {
        var match = Regex.Match(html, @"window\.insider_object\s*=\s*(\{.*?\});", RegexOptions.Singleline);
        if (!match.Success) return null;

        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            if (!doc.RootElement.TryGetProperty("product", out var pEl)) return null;

            var dto = new ProductDto { ProductUrl = pageUrl };

            if (pEl.TryGetProperty("name", out var nameEl))
                dto.Title = nameEl.GetString() ?? "";

            if (pEl.TryGetProperty("id", out var idEl))
            {
                var idStr = idEl.GetString() ?? "";
                var matchId = Regex.Match(idStr, @"^\d+");
                if (matchId.Success && long.TryParse(matchId.Value, out long parsedId))
                    dto.ExternalId = parsedId;
            }

            if (pEl.TryGetProperty("unit_sale_price", out var salePriceEl))
                dto.Price = ParsePriceElement(salePriceEl);

            if (pEl.TryGetProperty("unit_price", out var unitPriceEl))
                dto.RegularPrice = ParsePriceElement(unitPriceEl);

            if (dto.RegularPrice < dto.Price) dto.RegularPrice = dto.Price;

            if (pEl.TryGetProperty("product_image_url", out var imgEl))
                dto.ImageUrl = imgEl.GetString();

            if (pEl.TryGetProperty("taxonomy", out var taxEl) && taxEl.ValueKind == JsonValueKind.Array)
            {
                var cats = taxEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (cats.Count > 0) dto.RootCategoryName = cats[0];
                if (cats.Count > 1) dto.ParentCategoryName = cats[1];
                if (cats.Count > 2) dto.CategoryName = cats[cats.Count - 1];
            }

            if (pEl.TryGetProperty("custom", out var customEl) && customEl.TryGetProperty("shop_name", out var shopEl))
            {
                dto.StoreName = shopEl.GetString();
            }

            if (!string.IsNullOrWhiteSpace(dto.Title) && dto.ExternalId != 0 && dto.Price > 0)
                return dto;
        }
        catch
        {
            // Ignore fallback failure
        }

        return null;
    }

    private static ProductDto? ParseProductFromDataLayerOrMeta(string html, string pageUrl)
    {
        var dataLayerMatch = Regex.Match(html, @"<div[^>]*id=""datalayer-data""(.*?)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!dataLayerMatch.Success) return null;

        var attrs = dataLayerMatch.Groups[1].Value;
        var dto = new ProductDto { ProductUrl = pageUrl };

        var nameMatch = Regex.Match(attrs, @"data-product-name=""([^""]+)""");
        if (nameMatch.Success) dto.Title = nameMatch.Groups[1].Value;

        var idMatch = Regex.Match(attrs, @"data-product-id=""([^""]+)""");
        if (idMatch.Success && long.TryParse(idMatch.Groups[1].Value, out long extId)) dto.ExternalId = extId;

        var brandMatch = Regex.Match(attrs, @"data-product-brand=""([^""]+)""");
        if (brandMatch.Success) dto.Brand = brandMatch.Groups[1].Value;

        var priceMatch = Regex.Match(attrs, @"data-product-price=""([^""]+)""");
        if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
        {
            dto.Price = price;
            dto.RegularPrice = price;
        }

        var storeMatch = Regex.Match(attrs, @"data-shop-name=""([^""]+)""");
        if (storeMatch.Success) dto.StoreName = storeMatch.Groups[1].Value;

        var catStructureMatch = Regex.Match(attrs, @"data-product-category-structure=""([^""]+)""");
        if (catStructureMatch.Success)
        {
            var parts = catStructureMatch.Groups[1].Value.Split('>').Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p) && !p.Equals("Kategoriler", StringComparison.OrdinalIgnoreCase)).ToList();
            if (parts.Count > 0) dto.RootCategoryName = parts[0];
            if (parts.Count > 1) dto.ParentCategoryName = parts[1];
            if (parts.Count > 2) dto.CategoryName = parts[parts.Count - 1];
        }

        if (!string.IsNullOrWhiteSpace(dto.Title) && dto.ExternalId != 0 && dto.Price > 0)
            return dto;

        return null;
    }

    private static decimal ParsePriceElement(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out decimal numVal))
            return numVal;

        if (el.ValueKind == JsonValueKind.String)
        {
            var str = el.GetString() ?? "";
            return ParsePriceString(str);
        }

        return 0m;
    }

    private static decimal ParsePriceString(string priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText)) return 0m;

        var cleaned = Regex.Replace(priceText, @"[^\d.,]", "").Trim();
        if (string.IsNullOrEmpty(cleaned)) return 0m;

        if (cleaned.Contains(',') && cleaned.Contains('.'))
        {
            cleaned = cleaned.Replace(".", "").Replace(',', '.');
        }
        else if (cleaned.Contains(','))
        {
            cleaned = cleaned.Replace(',', '.');
        }
        else if (cleaned.Contains('.'))
        {
            var parts = cleaned.Split('.');
            if (parts.Length == 2 && parts[1].Length == 3 && parts[0].Length <= 3)
            {
                cleaned = cleaned.Replace(".", "");
            }
        }

        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
        {
            return result;
        }

        return 0m;
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        return Regex.Replace(input, @"<[^>]*>", " ");
    }

    public async ValueTask DisposeAsync()
    {
        if (_page != null)
        {
            await _page.CloseAsync();
            _page = null;
        }
        if (_context != null)
        {
            await _context.CloseAsync();
            _context = null;
        }
        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }
        if (_playwright != null)
        {
            _playwright.Dispose();
            _playwright = null;
        }
    }
}
