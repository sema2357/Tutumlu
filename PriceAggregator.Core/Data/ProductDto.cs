namespace PriceAggregator.Core.Data;

public class ProductDto
{
    public long ExternalId { get; set; }
    public string Title { get; set; } = "";
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public decimal RegularPrice { get; set; }
    public string ProductUrl { get; set; } = "";
    public string? Brand { get; set; }
    public string? Barcode { get; set; }

    public string? CategoryName { get; set; }
    public string? StoreCategoryId { get; set; }
    public string? StoreName { get; set; }
    public string? ParentCategoryName { get; set; }
    public string? RootCategoryName { get; set; }
    public List<string> CategoryPath { get; set; } = [];

    public string? SourceUnitType { get; set; }
    public decimal? SourceUnitAmount { get; set; }
    public decimal? SourceUnitPrice { get; set; }

    public Dictionary<string, string>? Attributes { get; set; }
}