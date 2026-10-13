using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Scrapers;

public class AvansasScraper : IProductScraper, IProductSpecScraper, IAsyncDisposable
{
    public string Name => "avansas";
    public ScraperSector Sector => ScraperSector.Market;

    private readonly HttpClient? _httpClient;
    private const int DefaultMaxPages = 5;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _browserLock = new(1, 1);

    public AvansasScraper()
    {
    }

    public AvansasScraper(HttpClient httpClient)
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
                try
                {
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
                catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist") || ex.Message.Contains("Please run the following command"))
                {
                    Logger.Log("[AvansasScraper] Playwright Chromium tarayıcısı eksik. Otomatik kuruluyor, lütfen bekleyin...");
                    int exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                    Logger.Log($"[AvansasScraper] Playwright kurulum sonucu: {exitCode}");
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
        }
        finally
        {
            _browserLock.Release();
        }
    }

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
                Locale = "tr-TR",
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8" },
                    { "Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7" },
                    { "sec-ch-ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"" },
                    { "sec-ch-ua-mobile", "?0" },
                    { "sec-ch-ua-platform", "\"Windows\"" }
                }
            });
            page = await context.NewPageAsync();

            var options = new PageGotoOptions
            {
                Timeout = 30000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            };

            await page.GotoAsync(url, options);
            await page.WaitForTimeoutAsync(2000);
            return await page.ContentAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"[AvansasScraper] Navigation error for {url}: {ex.Message}");
            return null;
        }
        finally
        {
            if (page != null) await page.CloseAsync();
            if (context != null) await context.CloseAsync();
        }
    }

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm) ||
            searchTerm.Equals("ALL_CATEGORIES", StringComparison.OrdinalIgnoreCase) ||
            searchTerm.Equals("avansas_catalog", StringComparison.OrdinalIgnoreCase))
        {
            return await FetchCategoryProductsAsync(maxCategories: 500, maxProductsPerCategory: 300);
        }
        return await FetchProductsAsync(searchTerm, DefaultMaxPages);
    }

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm, int maxPages)
    {
        var results = new List<ProductDto>();
        if (string.IsNullOrWhiteSpace(searchTerm)) return results;

        var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedSkus = new HashSet<long>();

        for (int pageIndex = 1; pageIndex <= maxPages; pageIndex++)
        {
            var searchUrl = $"https://www.avansas.com/arama?q={Uri.EscapeDataString(searchTerm)}&page={pageIndex}";
            string? html = await FetchHtmlAsync(searchUrl, referer: "https://www.avansas.com/");

            if (string.IsNullOrWhiteSpace(html)) break;

            var pageProducts = ExtractProductsFromListingHtml(html, searchUrl);
            if (pageProducts.Count == 0) break;

            int newOnPage = 0;
            foreach (var p in pageProducts)
            {
                if (p.ExternalId != 0 && !processedSkus.Add(p.ExternalId)) continue;
                if (!string.IsNullOrWhiteSpace(p.ProductUrl) && !processedUrls.Add(p.ProductUrl)) continue;

                results.Add(p);
                newOnPage++;
            }

            if (newOnPage == 0) break;
            await Task.Delay(200);
        }

        return results;
    }

    public async Task<List<(string Name, string Url, string CategoryId)>> DiscoverCategoriesAsync()
    {
        var categories = new List<(string Name, string Url, string CategoryId)>();
        var rootUrl = "https://www.avansas.com/";
        string? html = await FetchHtmlAsync(rootUrl);

        if (string.IsNullOrWhiteSpace(html))
        {
            Logger.Log("[AvansasScraper] Failed to fetch root homepage for category discovery.");
            return categories;
        }

        var matches = Regex.Matches(html, @"href=""([^""]*?-c-(\d+))""[^>]*?>([^<]+)<", RegexOptions.IgnoreCase);
        var seenIds = new HashSet<string>();

        foreach (Match m in matches)
        {
            var rawUrl = m.Groups[1].Value.Trim();
            var catId = m.Groups[2].Value.Trim();
            var name = Regex.Replace(m.Groups[3].Value, @"<[^>]+>", "").Trim();

            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;
            if (!seenIds.Add(catId)) continue;

            var fullUrl = rawUrl.StartsWith("http") ? rawUrl : $"https://www.avansas.com{rawUrl}";
            categories.Add((name, fullUrl, catId));
        }

        Logger.Log($"[AvansasScraper] Discovered {categories.Count} category endpoints.");
        return categories;
    }

    public async Task<List<ProductDto>> FetchCategoryProductsAsync(int maxCategories = 500, int maxProductsPerCategory = 300)
    {
        var results = new List<ProductDto>();
        var discoveredCategories = await DiscoverCategoriesAsync();

        if (discoveredCategories.Count == 0) return results;

        var processedSkus = new HashSet<long>();
        var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var targetCategories = discoveredCategories.Take(maxCategories).ToList();

        for (int i = 0; i < targetCategories.Count; i++)
        {
            var category = targetCategories[i];
            int extractedForThisCategory = 0;
            int pageIndex = 1;
            int maxPages = (int)Math.Ceiling(maxProductsPerCategory / 20.0);

            while (extractedForThisCategory < maxProductsPerCategory && pageIndex <= maxPages)
            {
                var categoryPageUrl = BuildCategoryPageUrl(category.Url, pageIndex);
                string? html = await FetchHtmlAsync(categoryPageUrl, referer: "https://www.avansas.com/");

                if (string.IsNullOrWhiteSpace(html)) break;

                var categoryProducts = ExtractProductsFromListingHtml(html, categoryPageUrl);
                if (categoryProducts.Count == 0) break;

                int newOnPage = 0;
                foreach (var p in categoryProducts)
                {
                    if (extractedForThisCategory >= maxProductsPerCategory) break;

                    if (p.ExternalId != 0 && !processedSkus.Add(p.ExternalId)) continue;
                    if (!string.IsNullOrWhiteSpace(p.ProductUrl) && !processedUrls.Add(p.ProductUrl)) continue;

                    if (string.IsNullOrWhiteSpace(p.CategoryName)) p.CategoryName = category.Name;
                    results.Add(p);
                    extractedForThisCategory++;
                    newOnPage++;
                }

                if (newOnPage == 0) break;
                pageIndex++;
                await Task.Delay(200);
            }
        }

        return results;
    }

    public List<ProductDto> ExtractProductsFromListingHtml(string html, string pageUrl)
    {
        var products = new List<ProductDto>();
        if (string.IsNullOrWhiteSpace(html)) return products;

        var breadcrumbs = PriceAggregator.Core.Categorization.HtmlBreadcrumbExtractor.Extract(html);
        var (rootCat, parentCat, leafCat) = PriceAggregator.Core.Categorization.HtmlBreadcrumbExtractor.ToHierarchy(breadcrumbs);

        var scriptMatches = Regex.Matches(html, @"<script[^>]*?type=""application/ld\+json""[^>]*?>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match m in scriptMatches)
        {
            try
            {
                var json = m.Groups[1].Value.Trim();
                if (json.Contains("Product"))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    ExtractProductsFromJsonElement(root, pageUrl, products);
                }
            }
            catch { }
        }

        if (products.Count == 0)
        {
            var cardMatches = Regex.Matches(html, @"<div[^>]*?class=""[^""]*?(?:product-list-item|product-card|product-box|product-item)[^""]*?""[^>]*?>(.*?)</div>\s*</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in cardMatches)
            {
                var cardHtml = m.Value;
                var dto = ParseProductHtmlCard(cardHtml, pageUrl);
                if (dto != null) products.Add(dto);
            }
        }

        foreach (var p in products)
        {
            if (string.IsNullOrWhiteSpace(p.CategoryName) && !string.IsNullOrWhiteSpace(leafCat))
                p.CategoryName = leafCat;
            if (string.IsNullOrWhiteSpace(p.ParentCategoryName) && !string.IsNullOrWhiteSpace(parentCat))
                p.ParentCategoryName = parentCat;
            if (string.IsNullOrWhiteSpace(p.RootCategoryName) && !string.IsNullOrWhiteSpace(rootCat))
                p.RootCategoryName = rootCat;
        }

        return products;
    }

    private void ExtractProductsFromJsonElement(JsonElement elem, string pageUrl, List<ProductDto> products)
    {
        if (elem.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in elem.EnumerateArray())
            {
                ExtractProductsFromJsonElement(child, pageUrl, products);
            }
        }
        else if (elem.ValueKind == JsonValueKind.Object)
        {
            if (elem.TryGetProperty("@type", out var typeProp) && typeProp.GetString() == "Product")
            {
                var dto = ParseJsonLdProduct(elem, pageUrl);
                if (dto != null) products.Add(dto);
            }
            else if (elem.TryGetProperty("item", out var itemProp) && itemProp.ValueKind == JsonValueKind.Object)
            {
                ExtractProductsFromJsonElement(itemProp, pageUrl, products);
            }
            else if (elem.TryGetProperty("itemListElement", out var listProp))
            {
                ExtractProductsFromJsonElement(listProp, pageUrl, products);
            }
        }
    }

    private ProductDto? ParseJsonLdProduct(JsonElement elem, string pageUrl)
    {
        try
        {
            string? name = elem.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) return null;

            string? skuStr = elem.TryGetProperty("sku", out var s) ? s.GetString() : null;
            long externalId = 0;
            if (!string.IsNullOrWhiteSpace(skuStr))
            {
                var digits = Regex.Replace(skuStr, @"[^\d]", "");
                long.TryParse(digits, out externalId);
            }

            decimal price = 0;
            if (elem.TryGetProperty("offers", out var offers))
            {
                if (offers.ValueKind == JsonValueKind.Object && offers.TryGetProperty("price", out var p))
                {
                    decimal.TryParse(p.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out price);
                }
                else if (offers.ValueKind == JsonValueKind.Array && offers.GetArrayLength() > 0)
                {
                    var firstOffer = offers[0];
                    if (firstOffer.TryGetProperty("price", out var p2))
                    {
                        decimal.TryParse(p2.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out price);
                    }
                }
            }
            else if (elem.TryGetProperty("price", out var pDirect))
            {
                decimal.TryParse(pDirect.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out price);
            }

            if (price <= 0) return null;

            string? brand = null;
            if (elem.TryGetProperty("brand", out var b))
            {
                if (b.ValueKind == JsonValueKind.Object && b.TryGetProperty("name", out var bn))
                    brand = bn.GetString();
                else if (b.ValueKind == JsonValueKind.String)
                    brand = b.GetString();
            }

            string? image = null;
            if (elem.TryGetProperty("image", out var img))
            {
                if (img.ValueKind == JsonValueKind.Array && img.GetArrayLength() > 0)
                    image = img[0].GetString();
                else if (img.ValueKind == JsonValueKind.String)
                    image = img.GetString();
            }

            string? url = elem.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) && elem.TryGetProperty("@id", out var idProp))
            {
                url = idProp.GetString();
                if (url != null && url.EndsWith("#product")) url = url.Substring(0, url.Length - 8);
            }

            if (externalId == 0 && !string.IsNullOrWhiteSpace(url))
            {
                var codeMatch = Regex.Match(url, @"-p-(\d+)");
                if (codeMatch.Success) long.TryParse(codeMatch.Groups[1].Value, out externalId);
            }

            return new ProductDto
            {
                ExternalId = externalId,
                Title = name.Trim(),
                Brand = !string.IsNullOrWhiteSpace(brand) ? brand.Trim() : "Avansas",
                Price = price,
                RegularPrice = price,
                StoreName = "Avansas",
                ProductUrl = url != null && url.StartsWith("http") ? url : $"https://www.avansas.com{url}",
                ImageUrl = image,
                CategoryName = null,
                ParentCategoryName = null,
                RootCategoryName = null
            };
        }
        catch
        {
            return null;
        }
    }

    private ProductDto? ParseProductHtmlCard(string cardHtml, string pageUrl)
    {
        try
        {
            var titleMatch = Regex.Match(cardHtml, @"class=""[^""]*?product-name[^""]*?""[^>]*?>([^<]+)<", RegexOptions.IgnoreCase);
            if (!titleMatch.Success) titleMatch = Regex.Match(cardHtml, @"title=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!titleMatch.Success) return null;

            var title = titleMatch.Groups[1].Value.Trim();

            var priceMatch = Regex.Match(cardHtml, @"([\d\.,]+)\s*TL", RegexOptions.IgnoreCase);
            if (!priceMatch.Success) return null;

            var priceStr = priceMatch.Groups[1].Value.Replace(".", "").Replace(",", ".");
            if (!decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price) || price <= 0) return null;

            var linkMatch = Regex.Match(cardHtml, @"href=""([^""]+?)""", RegexOptions.IgnoreCase);
            var productUrl = linkMatch.Success ? linkMatch.Groups[1].Value : pageUrl;
            if (!productUrl.StartsWith("http")) productUrl = $"https://www.avansas.com{productUrl}";

            var imgMatch = Regex.Match(cardHtml, @"src=""([^""]+?)""", RegexOptions.IgnoreCase);
            var imageUrl = imgMatch.Success ? imgMatch.Groups[1].Value : null;

            long externalId = 0;
            var codeMatch = Regex.Match(productUrl, @"-p-(\d+)");
            if (codeMatch.Success) long.TryParse(codeMatch.Groups[1].Value, out externalId);

            return new ProductDto
            {
                ExternalId = externalId,
                Title = title,
                Brand = "Avansas",
                Price = price,
                RegularPrice = price,
                StoreName = "Avansas",
                ProductUrl = productUrl,
                ImageUrl = imageUrl,
                CategoryName = null,
                ParentCategoryName = null,
                RootCategoryName = null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string BuildCategoryPageUrl(string baseUrl, int pageIndex)
    {
        if (pageIndex <= 1) return baseUrl;
        if (baseUrl.Contains("?"))
        {
            if (Regex.IsMatch(baseUrl, @"page=\d+"))
            {
                return Regex.Replace(baseUrl, @"page=\d+", $"page={pageIndex}");
            }
            return $"{baseUrl}&page={pageIndex}";
        }
        return $"{baseUrl}?page={pageIndex}";
    }

    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        return new List<ProductSpec>();
    }

    public async Task<ProductDto?> FetchProductDetailAsync(string productUrl)
    {
        string? html = await FetchHtmlAsync(productUrl);
        if (string.IsNullOrWhiteSpace(html)) return null;

        var products = ExtractProductsFromListingHtml(html, productUrl);
        return products.Count > 0 ? products[0] : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        if (_playwright != null) _playwright.Dispose();
    }
}
