using System.Text.Json;

namespace PriceAggregator.Core.Scrapers;

public class CagriMarketScraper : IProductScraper
{
    public string Name => "cagrimarket";
    public ScraperSector Sector => ScraperSector.Market;

    private readonly HttpClient _httpClient;

    public CagriMarketScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Remove("Accept");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.Remove("Accept-Language");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Remove("Referer");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.cagri.com/");
        _httpClient.DefaultRequestHeaders.Remove("Origin");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.cagri.com");
    }

    public async Task<List<ProductDto>> FetchProductsAsync(string searchTerm)
    {
        return await FetchProductsAsync(10);
    }

    public async Task<List<ProductDto>> FetchProductsAsync(long categoryId = 10)
    {
        var url = $"https://api.cagri.com/category/breadcrumb/{categoryId}";

        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseText);

        var results = new List<ProductDto>();

        // Gelen JSON'ın kök yapısına göre (örneğin direkt dizi mi yoksa içinde data nesnesi mi var) 
        // buradaki property yolunu gelen yanıta göre esnetebilirsin.
        JsonElement dataArray = json.RootElement;

        // Eğer dizi değil de objenin içindeyse örn: json.RootElement.GetProperty("data") gibi ele alabilirsin.
        if (dataArray.ValueKind != JsonValueKind.Array)
        {
            // Eğer kök dizi değilse, yaygın api standartlarında data/products alanı aranır:
            if (!json.RootElement.TryGetProperty("data", out dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                return results;
        }

        foreach (var p in dataArray.EnumerateArray())
        {
            long externalId = p.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var id) ? id : 0;
            string title = p.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            string productUrl = p.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";

            decimal price = p.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var pr) ? pr : 0;
            decimal regularPrice = p.TryGetProperty("oldPrice", out var oldPriceProp) && oldPriceProp.TryGetDecimal(out var opr)
                ? opr
                : price;

            string? imageUrl = null;
            if (p.TryGetProperty("image", out var imgProp))
            {
                imageUrl = imgProp.GetString();
            }

            results.Add(new ProductDto
            {
                ExternalId = externalId,
                Title = title,
                ImageUrl = imageUrl,
                Price = price,
                RegularPrice = regularPrice,
                ProductUrl = productUrl
            });
        }

        return results;
    }
}