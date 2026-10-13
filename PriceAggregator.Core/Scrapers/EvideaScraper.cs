using System.Text;
using System.Text.Json;
using PriceAggregator.Core.Data;

namespace PriceAggregator.Core.Scrapers;

public class EvideaScraper : IProductScraper, IProductSpecScraper
{
    public string Name => "evidea";
    public ScraperSector Sector => ScraperSector.Mobilya;

    private readonly HttpClient _httpClient;

    public EvideaScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Remove("Accept");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.Remove("Accept-Language");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Remove("Referer");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.evidea.com/");
        _httpClient.DefaultRequestHeaders.Remove("Origin");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.evidea.com");
    }

    private const int PageSize = 24;
    // Already stops early once the store's own totalPages is reached (see FetchProductsAsync
    // below) — raising the ceiling only lets broader terms pull more of what's actually there.
    private const int MaxPages = 5;

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        var results = new List<ProductDto>();

        for (int pageIndex = 1; pageIndex <= MaxPages; pageIndex++)
        {
            var url = "https://api.tr.primewidgets.com/products/searchv5/evidea?=";

            var requestBody = new
            {
                search = searchTerm,
                categories = Array.Empty<string>(),
                brands = Array.Empty<string>(),
                attributes = new[]
                {
                    new { name = "color", values = Array.Empty<string>() },
                    new { name = "gender", values = Array.Empty<string>() }
                },
                filter = new { orderBy = "DESC", sortBy = "sales.monthly" },
                paging = new { index = pageIndex, size = PageSize.ToString() },
                price = new { min = (decimal?)null, max = (decimal?)null },
                status = "ACTIVE"
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(responseText);

            // results.products.data
            if (!json.RootElement.TryGetProperty("results", out var resultsEl)) break;
            if (!resultsEl.TryGetProperty("products", out var productsEl)) break;
            if (!productsEl.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                break;

            foreach (var p in dataArray.EnumerateArray())
            {
                string code = p.GetProperty("code").GetString() ?? "";
                string title = p.GetProperty("name").GetString() ?? "";
                string productUrl = p.GetProperty("url").GetString() ?? "";

                string brand = p.TryGetProperty("brand", out var brandEl) && brandEl.ValueKind == JsonValueKind.Object
                    ? brandEl.GetProperty("name").GetString() ?? ""
                    : "";

                decimal price = p.GetProperty("price").GetDecimal();
                decimal regularPrice = p.TryGetProperty("oldPrice", out var oldPriceProp) && oldPriceProp.ValueKind != JsonValueKind.Null
                    ? oldPriceProp.GetDecimal()
                    : price;

                string? imageUrl = null;
                if (p.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0)
                {
                    imageUrl = images[0].GetString();
                }

                // Sayisal urun ID'si "attributes" dizisi icinde name == "pk" olan elemanin value'sunda.
                // Attributes also carry "product_type", "integration_Ana_Kategori", "integration_Kategori", "integration_Alt_Kategori"
                long externalId = 0;
                string? productType = null;
                string? color = null;
                string? material = null;
                string? integrationAnaKat = null;
                string? integrationKat = null;
                string? integrationAltKat = null;

                if (p.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var attr in attributes.EnumerateArray())
                    {
                        if (!attr.TryGetProperty("name", out var attrName)) continue;
                        var an = attrName.GetString();

                        if (an == "pk" && attr.TryGetProperty("value", out var pkValue) &&
                            long.TryParse(pkValue.GetString(), out var parsedPk))
                        {
                            externalId = parsedPk;
                        }
                        else if (an == "product_type" && attr.TryGetProperty("value", out var ptValue))
                        {
                            productType = ptValue.GetString();
                        }
                        else if (an == "color" && attr.TryGetProperty("value", out var colorValue))
                        {
                            color = colorValue.GetString();
                        }
                        else if (an == "material" && attr.TryGetProperty("value", out var materialValue))
                        {
                            material = materialValue.GetString();
                        }
                        else if (an is "integration_Ana_Kategori" or "erp_Ana_Kategori" && attr.TryGetProperty("value", out var iakVal))
                        {
                            integrationAnaKat = iakVal.GetString();
                        }
                        else if (an is "integration_Kategori" or "erp_Kategori" && attr.TryGetProperty("value", out var ikVal))
                        {
                            integrationKat = ikVal.GetString();
                        }
                        else if (an is "integration_Alt_Kategori" or "erp_Alt_Kategori" && attr.TryGetProperty("value", out var ialtVal))
                        {
                            integrationAltKat = ialtVal.GetString();
                        }
                    }
                }

                Dictionary<string, string>? productAttributes = null;
                if (!string.IsNullOrWhiteSpace(color) || !string.IsNullOrWhiteSpace(material))
                {
                    productAttributes = new Dictionary<string, string>();
                    if (!string.IsNullOrWhiteSpace(color)) productAttributes["Renk"] = color;
                    if (!string.IsNullOrWhiteSpace(material)) productAttributes["Malzeme"] = material;
                }

                var categoryPath = new List<string>();
                string? categoryName = null, parentCategoryName = null, rootCategoryName = null;
                if (!string.IsNullOrWhiteSpace(productType))
                {
                    var parts = productType.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    categoryPath.AddRange(parts);
                    if (parts.Length >= 3)
                    {
                        rootCategoryName = parts[0];
                        parentCategoryName = parts[1];
                        categoryName = parts[^1];
                    }
                    else if (parts.Length == 2)
                    {
                        parentCategoryName = parts[0];
                        categoryName = parts[1];
                    }
                    else if (parts.Length == 1)
                    {
                        categoryName = parts[0];
                    }
                }

                if (string.IsNullOrWhiteSpace(rootCategoryName) && !string.IsNullOrWhiteSpace(integrationAnaKat))
                {
                    rootCategoryName = integrationAnaKat;
                    if (!categoryPath.Contains(integrationAnaKat, StringComparer.OrdinalIgnoreCase)) categoryPath.Insert(0, integrationAnaKat);
                }
                if (string.IsNullOrWhiteSpace(parentCategoryName) && !string.IsNullOrWhiteSpace(integrationKat))
                {
                    parentCategoryName = integrationKat;
                    if (!categoryPath.Contains(integrationKat, StringComparer.OrdinalIgnoreCase)) categoryPath.Add(integrationKat);
                }
                if (string.IsNullOrWhiteSpace(categoryName) && !string.IsNullOrWhiteSpace(integrationAltKat))
                {
                    categoryName = integrationAltKat;
                    if (!categoryPath.Contains(integrationAltKat, StringComparer.OrdinalIgnoreCase)) categoryPath.Add(integrationAltKat);
                }

                if (categoryName is null && p.TryGetProperty("categories", out var cats) &&
                    cats.ValueKind == JsonValueKind.Array && cats.GetArrayLength() > 0)
                {
                    rootCategoryName = cats[0].TryGetProperty("name", out var catName) ? catName.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(rootCategoryName) && !categoryPath.Contains(rootCategoryName, StringComparer.OrdinalIgnoreCase))
                        categoryPath.Add(rootCategoryName);
                }

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
                    CategoryPath = categoryPath,
                    Attributes = productAttributes
                });
            }

            var totalPages = productsEl.TryGetProperty("totalPages", out var totalPagesEl) ? totalPagesEl.GetInt32() : 1;
            if (dataArray.GetArrayLength() == 0 || pageIndex >= totalPages) break;
        }

        return results;
    }

    private static readonly (string ErpKey, string IntegrationKey, string SpecName)[] EvideaSpecFields =
    [
        ("erp_materyal", "integration_materyal", "Malzeme/Materyal"),
        ("erp_olcu", "integration_olcu", "Ölçü"),
        ("erp_ColorDesc", "integration_ColorDesc", "Renk"),
        ("erp_mensei", "integration_mensei", "Menşei"),
    ];

    public async Task<List<ProductSpec>> FetchSpecsAsync(ProductDto product)
    {
        if (string.IsNullOrWhiteSpace(product.ProductUrl)) return [];

        try
        {
            var response = await _httpClient.GetStringAsync(product.ProductUrl);
            var trimmed = response.Trim();

            // 1. If response is JSON payload
            if (trimmed.StartsWith('{'))
            {
                using var json = JsonDocument.Parse(trimmed);
                if (json.RootElement.TryGetProperty("product", out var productEl))
                {
                    // Enrich category information from product metadata if missing
                    EnrichCategoryFromJson(product, productEl);

                    if (productEl.TryGetProperty("attributes", out var attributes))
                    {
                        var specs = new List<ProductSpec>();
                        foreach (var (erpKey, integrationKey, specName) in EvideaSpecFields)
                        {
                            var value = ReadStringAttribute(attributes, erpKey) ?? ReadStringAttribute(attributes, integrationKey);
                            if (!string.IsNullOrWhiteSpace(value))
                                specs.Add(new ProductSpec("Genel Bilgiler", specName, value));
                        }
                        return specs;
                    }
                }
            }
            else
            {
                // 2. If response is HTML, extract breadcrumbs via HtmlBreadcrumbExtractor
                var breadcrumbs = PriceAggregator.Core.Categorization.HtmlBreadcrumbExtractor.Extract(response);
                var (root, parent, leaf) = PriceAggregator.Core.Categorization.HtmlBreadcrumbExtractor.ToHierarchy(breadcrumbs);
                if (!string.IsNullOrWhiteSpace(leaf))
                {
                    product.RootCategoryName = root;
                    product.ParentCategoryName = parent;
                    product.CategoryName = leaf;
                    product.CategoryPath = breadcrumbs;
                }
            }

            return [];
        }
        catch (Exception ex)
        {
            Logger.Log($"[Evidea] Failed to fetch specs for product {product.ExternalId}: {ex.Message}");
            return [];
        }
    }

    private static void EnrichCategoryFromJson(ProductDto product, JsonElement productEl)
    {
        string? root = null, parent = null, leaf = null;

        // Check attributes_kwargs for category labels
        if (productEl.TryGetProperty("attributes_kwargs", out var kwargs) && kwargs.ValueKind == JsonValueKind.Object)
        {
            if (kwargs.TryGetProperty("integration_Ana_Kategori", out var iak) && iak.TryGetProperty("label", out var iakLabel))
                root = iakLabel.GetString();
            if (kwargs.TryGetProperty("integration_Kategori", out var ik) && ik.TryGetProperty("label", out var ikLabel))
                parent = ikLabel.GetString();
            if (kwargs.TryGetProperty("integration_Alt_Kategori", out var ialt) && ialt.TryGetProperty("label", out var ialtLabel))
                leaf = ialtLabel.GetString();
        }

        // Fallback to attributes dictionary
        if (productEl.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            root ??= ReadStringAttribute(attrs, "erp_Ana_Kategori") ?? ReadStringAttribute(attrs, "integration_Ana_Kategori");
            parent ??= ReadStringAttribute(attrs, "erp_Kategori") ?? ReadStringAttribute(attrs, "integration_Kategori");
            leaf ??= ReadStringAttribute(attrs, "erp_Alt_Kategori") ?? ReadStringAttribute(attrs, "integration_Alt_Kategori");
        }

        if (!string.IsNullOrWhiteSpace(leaf))
        {
            product.RootCategoryName ??= root;
            product.ParentCategoryName ??= parent;
            product.CategoryName ??= leaf;
        }
    }

    private static string? ReadStringAttribute(JsonElement attributes, string key) =>
        attributes.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()?.Trim()
            : null;
}