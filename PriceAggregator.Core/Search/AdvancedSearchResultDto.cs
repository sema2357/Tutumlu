namespace PriceAggregator.Core.Search;

public class FacetCountModel
{
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class PriceRangeFacetModel
{
    public string Key { get; set; } = string.Empty;
    public double? From { get; set; }
    public double? To { get; set; }
    public long Count { get; set; }
}

public class AdvancedSearchResultDto
{
    public List<int> ProductIds { get; set; } = new();
    public List<ProductIndexModel> Products { get; set; } = new();
    public long TotalCount { get; set; }
    public string? DidYouMean { get; set; }
    public List<FacetCountModel> BrandFacets { get; set; } = new();
    public List<FacetCountModel> CategoryFacets { get; set; } = new();
    public List<PriceRangeFacetModel> PriceRangeFacets { get; set; } = new();
}
