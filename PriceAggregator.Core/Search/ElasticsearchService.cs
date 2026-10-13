using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace PriceAggregator.Core.Search;

public class ProductIndexModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? SectorId { get; set; }
    public string? ImageUrl { get; set; }
    public decimal LowestPrice { get; set; }
    public decimal? RegularPrice { get; set; }
    public bool IsOnSale { get; set; }
    public int OfferCount { get; set; }
}

public class ElasticsearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _indexName;

    public ElasticsearchService(IConfiguration configuration)
    {
        var uriStr = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
        _indexName = configuration["Elasticsearch:DefaultIndex"] ?? "tutumlu_products";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(uriStr.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var username = configuration["Elasticsearch:Username"] ?? "elastic";
        var password = configuration["Elasticsearch:Password"];

        if (Uri.TryCreate(uriStr, UriKind.Absolute, out var parsedUri) && !string.IsNullOrEmpty(parsedUri.UserInfo))
        {
            var parts = parsedUri.UserInfo.Split(':');
            username = parts[0];
            if (parts.Length > 1) password = parts[1];
        }

        if (!string.IsNullOrEmpty(password))
        {
            var authBytes = Encoding.UTF8.GetBytes($"{username}:{password}");
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }
    }

    public async Task<bool> IsClusterHealthyAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("_cluster/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureIndexCreatedAsync(bool recreateIfExists = false)
    {
        try
        {
            var checkResponse = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, _indexName));
            if (checkResponse.IsSuccessStatusCode)
            {
                if (recreateIfExists)
                {
                    await _httpClient.DeleteAsync(_indexName);
                }
                else
                {
                    return;
                }
            }

            var indexConfigJson = @"{
              ""settings"": {
                ""analysis"": {
                  ""normalizer"": {
                    ""turkish_normalizer"": {
                      ""type"": ""custom"",
                      ""filter"": [""turkish_lowercase"", ""trim""]
                    }
                  },
                  ""filter"": {
                    ""autocomplete_filter"": {
                      ""type"": ""edge_ngram"",
                      ""min_gram"": 2,
                      ""max_gram"": 15
                    },
                    ""turkish_synonyms"": {
                      ""type"": ""synonym"",
                      ""synonyms"": [
                        ""su, icme suyu, pet su, sise su"",
                        ""cay, dokme cay, demlik cay, poset cay"",
                        ""kalem, kursun kalem, versatil, uc"",
                        ""bilgisayar, laptop, dizustu, notebook"",
                        ""telefon, cep telefonu, mobil"",
                        ""deterjan, camasir deterjani, toz deterjan"",
                        ""sut, tam yagli sut, yarim yagli sut"",
                        ""yag, aycicek yagi, zeytinyagi"",
                        ""kagit, A4, fotokopi kagidi"",
                        ""kartus, toner, yazici kartusu"",
                        ""defter, kareli defter, cizgili defter""
                      ]
                    }
                  },
                  ""analyzer"": {
                    ""autocomplete_analyzer"": {
                      ""type"": ""custom"",
                      ""tokenizer"": ""standard"",
                      ""filter"": [""turkish_lowercase"", ""lowercase"", ""turkish_synonyms"", ""autocomplete_filter""]
                    },
                    ""search_analyzer"": {
                      ""type"": ""custom"",
                      ""tokenizer"": ""standard"",
                      ""filter"": [""turkish_lowercase"", ""lowercase"", ""turkish_synonyms""]
                    }
                  }
                }
              },
              ""mappings"": {
                ""properties"": {
                  ""id"": { ""type"": ""integer"" },
                  ""name"": {
                    ""type"": ""text"",
                    ""analyzer"": ""autocomplete_analyzer"",
                    ""search_analyzer"": ""search_analyzer"",
                    ""fields"": {
                      ""keyword"": { ""type"": ""keyword"", ""normalizer"": ""turkish_normalizer"" },
                      ""standard"": { ""type"": ""text"", ""analyzer"": ""standard"" }
                    }
                  },
                  ""brandId"": { ""type"": ""integer"" },
                  ""brandName"": {
                    ""type"": ""text"",
                    ""analyzer"": ""autocomplete_analyzer"",
                    ""search_analyzer"": ""search_analyzer"",
                    ""fields"": {
                      ""keyword"": { ""type"": ""keyword"", ""normalizer"": ""turkish_normalizer"" }
                    }
                  },
                  ""categoryId"": { ""type"": ""integer"" },
                  ""categoryName"": {
                    ""type"": ""text"",
                    ""analyzer"": ""search_analyzer"",
                    ""fields"": {
                      ""keyword"": { ""type"": ""keyword"", ""normalizer"": ""turkish_normalizer"" }
                    }
                  },
                  ""sectorId"": { ""type"": ""integer"" },
                  ""imageUrl"": { ""type"": ""keyword"", ""index"": false },
                  ""lowestPrice"": { ""type"": ""double"" },
                  ""regularPrice"": { ""type"": ""double"" },
                  ""isOnSale"": { ""type"": ""boolean"" },
                  ""offerCount"": { ""type"": ""integer"" }
                }
              }
            }";

            var content = new StringContent(indexConfigJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(_indexName, content);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Logger.Log($"[Elasticsearch] Index creation response: {response.StatusCode} - {errorBody}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Elasticsearch] Index creation failed: {ex.Message}");
        }
    }

    public async Task<bool> IndexProductAsync(ProductIndexModel product)
    {
        try
        {
            await EnsureIndexCreatedAsync(false);
            var json = JsonSerializer.Serialize(product, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"{_indexName}/_doc/{product.Id}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Elasticsearch] IndexProduct failed for Product #{product.Id}: {ex.Message}");
            return false;
        }
    }

    public async Task<int> BulkIndexProductsAsync(IEnumerable<ProductIndexModel> products, bool recreateIndex = false)
    {
        try
        {
            await EnsureIndexCreatedAsync(recreateIndex);
            var list = products.ToList();
            if (list.Count == 0) return 0;

            int totalIndexed = 0;
            int batchSize = 500;
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            for (int i = 0; i < list.Count; i += batchSize)
            {
                var batch = list.Skip(i).Take(batchSize).ToList();
                var sb = new StringBuilder();

                foreach (var prod in batch)
                {
                    sb.Append("{\"index\":{\"_id\":\"").Append(prod.Id).Append("\"}}\n");
                    sb.Append(JsonSerializer.Serialize(prod, options)).Append("\n");
                }

                var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson");
                var response = await _httpClient.PostAsync($"{_indexName}/_bulk", content);
                if (response.IsSuccessStatusCode)
                {
                    totalIndexed += batch.Count;
                }
                else
                {
                    var errorStr = await response.Content.ReadAsStringAsync();
                    Logger.Log($"[Elasticsearch] BulkIndex batch error: {response.StatusCode} - {errorStr}");
                }
            }

            return totalIndexed;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Elasticsearch] BulkIndex failed: {ex.Message}");
            return 0;
        }
    }

    private static string NormalizeSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;

        var str = query;
        str = Regex.Replace(str, @"\b(\d+)\s*(kilo|kilogram|kg)\b", "$1 kg", RegexOptions.IgnoreCase);
        str = Regex.Replace(str, @"\b(\d+)\s*(litre|lt|l)\b", "$1 l", RegexOptions.IgnoreCase);
        str = Regex.Replace(str, @"\b(\d+)\s*(gram|gr|g)\b", "$1 g", RegexOptions.IgnoreCase);
        str = Regex.Replace(str, @"\b(\d+)\s*(mililitre|ml)\b", "$1 ml", RegexOptions.IgnoreCase);

        str = Regex.Replace(str, @"\bkilo\b", "kg", RegexOptions.IgnoreCase);
        str = Regex.Replace(str, @"\bkilogram\b", "kg", RegexOptions.IgnoreCase);
        str = Regex.Replace(str, @"\blitre\b", "l", RegexOptions.IgnoreCase);
        str = Regex.Replace(str, @"\bmililitre\b", "ml", RegexOptions.IgnoreCase);

        return str.Trim();
    }

    public async Task<AdvancedSearchResultDto> SearchProductsAdvancedAsync(
        string query,
        int? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int page = 1,
        int pageSize = 20)
    {
        var result = new AdvancedSearchResultDto();

        try
        {
            int from = (page - 1) * pageSize;

            var boolObj = new JsonObject();
            var mustArray = new JsonArray();
            var shouldArray = new JsonArray();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var normalizedQuery = NormalizeSearchQuery(query);

                var unitRegex = new Regex(@"\b(\d+(?:[\.,]\d+)?)\s*(kg|kilo|kilogram|g|gram|gr|l|lt|litre|ml|mililitre|adet|paket|li|lı|lu|lü)\b", RegexOptions.IgnoreCase);
                var unitMatch = unitRegex.Match(normalizedQuery);

                if (unitMatch.Success)
                {
                    var unitText = unitMatch.Value;
                    var productKeyword = unitRegex.Replace(normalizedQuery, "").Trim();

                    if (!string.IsNullOrWhiteSpace(productKeyword))
                    {
                        mustArray.Add(new JsonObject
                        {
                            ["multi_match"] = new JsonObject
                            {
                                ["query"] = productKeyword,
                                ["fields"] = new JsonArray { "name^6", "categoryName^5", "brandName^2" },
                                ["operator"] = "and",
                                ["fuzziness"] = productKeyword.Length > 3 ? "AUTO" : "0"
                            }
                        });

                        shouldArray.Add(new JsonObject
                        {
                            ["multi_match"] = new JsonObject
                            {
                                ["query"] = unitText,
                                ["fields"] = new JsonArray { "name^5" },
                                ["operator"] = "or"
                            }
                        });

                        var unitNumStr = unitMatch.Groups[1].Value;
                        if (double.TryParse(unitNumStr.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out double numVal))
                        {
                            var unitType = unitMatch.Groups[2].Value.ToLower();
                            if (unitType == "kg" || unitType == "kilo" || unitType == "kilogram")
                            {
                                var gramsStr = $"{numVal * 1000}g";
                                shouldArray.Add(new JsonObject { ["match"] = new JsonObject { ["name"] = new JsonObject { ["query"] = gramsStr, ["boost"] = 3 } } });
                            }
                            else if (unitType == "l" || unitType == "lt" || unitType == "litre")
                            {
                                var mlStr = $"{numVal * 1000}ml";
                                shouldArray.Add(new JsonObject { ["match"] = new JsonObject { ["name"] = new JsonObject { ["query"] = mlStr, ["boost"] = 3 } } });
                            }
                            else if (unitType == "ml" || unitType == "mililitre")
                            {
                                var lStr = $"{numVal / 1000.0}l";
                                shouldArray.Add(new JsonObject { ["match"] = new JsonObject { ["name"] = new JsonObject { ["query"] = lStr, ["boost"] = 3 } } });
                            }
                        }
                    }
                    else
                    {
                        mustArray.Add(new JsonObject
                        {
                            ["multi_match"] = new JsonObject
                            {
                                ["query"] = normalizedQuery,
                                ["fields"] = new JsonArray { "name^5" },
                                ["operator"] = "or"
                            }
                        });
                    }
                }
                else
                {
                    mustArray.Add(new JsonObject
                    {
                        ["multi_match"] = new JsonObject
                        {
                            ["query"] = normalizedQuery,
                            ["fields"] = new JsonArray { "categoryName^5", "name^4", "brandName^2" },
                            ["minimum_should_match"] = "2<60%",
                            ["fuzziness"] = normalizedQuery.Length > 3 ? "AUTO" : "0"
                        }
                    });
                }
            }
            else
            {
                mustArray.Add(new JsonObject
                {
                    ["match_all"] = new JsonObject()
                });
            }

            boolObj["must"] = mustArray;
            if (shouldArray.Count > 0)
            {
                boolObj["should"] = shouldArray;
            }

            var filterArray = new JsonArray();
            if (categoryId.HasValue)
            {
                filterArray.Add(new JsonObject
                {
                    ["term"] = new JsonObject
                    {
                        ["categoryId"] = categoryId.Value
                    }
                });
            }
            if (minPrice.HasValue || maxPrice.HasValue)
            {
                var rangeObj = new JsonObject();
                if (minPrice.HasValue) rangeObj["gte"] = (double)minPrice.Value;
                if (maxPrice.HasValue) rangeObj["lte"] = (double)maxPrice.Value;
                filterArray.Add(new JsonObject
                {
                    ["range"] = new JsonObject
                    {
                        ["lowestPrice"] = rangeObj
                    }
                });
            }
            if (filterArray.Count > 0)
            {
                boolObj["filter"] = filterArray;
            }

            var requestBody = new JsonObject
            {
                ["from"] = from,
                ["size"] = pageSize,
                ["query"] = new JsonObject { ["bool"] = boolObj },
                ["aggs"] = new JsonObject
                {
                    ["brands_facet"] = new JsonObject
                    {
                        ["terms"] = new JsonObject { ["field"] = "brandName.keyword", ["size"] = 10 }
                    },
                    ["categories_facet"] = new JsonObject
                    {
                        ["terms"] = new JsonObject { ["field"] = "categoryName.keyword", ["size"] = 10 }
                    },
                    ["price_ranges_facet"] = new JsonObject
                    {
                        ["range"] = new JsonObject
                        {
                            ["field"] = "lowestPrice",
                            ["ranges"] = new JsonArray
                            {
                                new JsonObject { ["key"] = "0 - 50 TL", ["to"] = 50 },
                                new JsonObject { ["key"] = "50 - 100 TL", ["from"] = 50, ["to"] = 100 },
                                new JsonObject { ["key"] = "100 - 500 TL", ["from"] = 100, ["to"] = 500 },
                                new JsonObject { ["key"] = "500 - 1000 TL", ["from"] = 500, ["to"] = 1000 },
                                new JsonObject { ["key"] = "1000+ TL", ["from"] = 1000 }
                            }
                        }
                    }
                },
                ["suggest"] = new JsonObject
                {
                    ["did_you_mean"] = new JsonObject
                    {
                        ["text"] = query,
                        ["term"] = new JsonObject
                        {
                            ["field"] = "name.standard",
                            ["suggest_mode"] = "missing",
                            ["min_word_length"] = 3
                        }
                    }
                }
            };

            var jsonContent = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_indexName}/_search", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                var responseStr = await response.Content.ReadAsStringAsync();
                var jsonNode = JsonNode.Parse(responseStr);

                if (jsonNode != null)
                {
                    var hitsObj = jsonNode["hits"];
                    if (hitsObj != null)
                    {
                        result.TotalCount = hitsObj["total"]?["value"]?.GetValue<long>() ?? 0;

                        var hitsArray = hitsObj["hits"]?.AsArray();
                        if (hitsArray != null)
                        {
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            foreach (var hit in hitsArray)
                            {
                                var source = hit?["_source"];
                                if (source != null)
                                {
                                    var product = JsonSerializer.Deserialize<ProductIndexModel>(source.ToJsonString(), options);
                                    if (product != null)
                                    {
                                        result.Products.Add(product);
                                        result.ProductIds.Add(product.Id);
                                    }
                                }
                            }
                        }
                    }

                    // Parse Aggregations
                    var aggsNode = jsonNode["aggregations"];
                    if (aggsNode != null)
                    {
                        // Brands
                        var brandBuckets = aggsNode["brands_facet"]?["buckets"]?.AsArray();
                        if (brandBuckets != null)
                        {
                            var brandMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                            foreach (var b in brandBuckets)
                            {
                                var rawKey = b?["key"]?.ToString();
                                var count = b?["doc_count"]?.GetValue<long>() ?? 0;
                                if (!string.IsNullOrWhiteSpace(rawKey))
                                {
                                    var titleKey = PriceAggregator.Core.Helpers.TurkishTextHelper.ToTitleCase(rawKey);
                                    brandMap[titleKey] = brandMap.GetValueOrDefault(titleKey) + count;
                                }
                            }
                            result.BrandFacets = brandMap
                                .Select(kvp => new FacetCountModel { Name = kvp.Key, Count = kvp.Value })
                                .OrderByDescending(f => f.Count)
                                .ToList();
                        }

                        // Categories
                        var catBuckets = aggsNode["categories_facet"]?["buckets"]?.AsArray();
                        if (catBuckets != null)
                        {
                            var catMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                            foreach (var b in catBuckets)
                            {
                                var rawKey = b?["key"]?.ToString();
                                var count = b?["doc_count"]?.GetValue<long>() ?? 0;
                                if (!string.IsNullOrWhiteSpace(rawKey))
                                {
                                    var titleKey = PriceAggregator.Core.Helpers.TurkishTextHelper.ToTitleCase(rawKey);
                                    catMap[titleKey] = catMap.GetValueOrDefault(titleKey) + count;
                                }
                            }
                            result.CategoryFacets = catMap
                                .Select(kvp => new FacetCountModel { Name = kvp.Key, Count = kvp.Value })
                                .OrderByDescending(f => f.Count)
                                .ToList();
                        }

                        // Price Ranges
                        var priceBuckets = aggsNode["price_ranges_facet"]?["buckets"]?.AsArray();
                        if (priceBuckets != null)
                        {
                            foreach (var b in priceBuckets)
                            {
                                var key = b?["key"]?.ToString();
                                var count = b?["doc_count"]?.GetValue<long>() ?? 0;
                                double? fromVal = b?["from"]?.GetValue<double>();
                                double? toVal = b?["to"]?.GetValue<double>();
                                if (!string.IsNullOrEmpty(key) && count > 0)
                                {
                                    result.PriceRangeFacets.Add(new PriceRangeFacetModel
                                    {
                                        Key = key,
                                        From = fromVal,
                                        To = toVal,
                                        Count = count
                                    });
                                }
                            }
                        }
                    }

                    // Parse Did You Mean Suggestion
                    var suggestNode = jsonNode["suggest"]?["did_you_mean"]?.AsArray();
                    if (suggestNode != null && suggestNode.Count > 0)
                    {
                        var optionsArr = suggestNode[0]?["options"]?.AsArray();
                        if (optionsArr != null && optionsArr.Count > 0)
                        {
                            var suggestedText = optionsArr[0]?["text"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(suggestedText) && !suggestedText.Equals(query, StringComparison.OrdinalIgnoreCase))
                            {
                                result.DidYouMean = suggestedText;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Elasticsearch] SearchProductsAdvancedAsync error: {ex.Message}");
        }

        return result;
    }

    public async Task<(List<string> Brands, List<object> Products)> GetSuggestionsAsync(string term)
    {
        var brands = new List<string>();
        var products = new List<object>();

        try
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
                return (brands, products);

            var requestBody = new JsonObject
            {
                ["from"] = 0,
                ["size"] = 5,
                ["query"] = new JsonObject
                {
                    ["multi_match"] = new JsonObject
                    {
                        ["query"] = term,
                        ["fields"] = new JsonArray { "brandName^4", "name^3" },
                        ["fuzziness"] = "AUTO"
                    }
                }
            };

            var jsonContent = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_indexName}/_search", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                var responseStr = await response.Content.ReadAsStringAsync();
                var jsonNode = JsonNode.Parse(responseStr);
                var hitsArray = jsonNode?["hits"]?["hits"]?.AsArray();

                if (hitsArray != null)
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    foreach (var hit in hitsArray)
                    {
                        var source = hit?["_source"];
                        if (source != null)
                        {
                            var product = JsonSerializer.Deserialize<ProductIndexModel>(source.ToJsonString(), options);
                            if (product != null)
                            {
                                if (!string.IsNullOrEmpty(product.BrandName) && !brands.Contains(product.BrandName))
                                {
                                    brands.Add(product.BrandName);
                                }
                                products.Add(new
                                {
                                    Id = product.Id,
                                    CanonicalTitle = product.Name,
                                    ImageUrl = product.ImageUrl
                                });
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Elasticsearch] GetSuggestionsAsync error: {ex.Message}");
        }

        return (brands.Take(5).ToList(), products.Take(5).ToList());
    }
}
