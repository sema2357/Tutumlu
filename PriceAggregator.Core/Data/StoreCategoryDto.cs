namespace PriceAggregator.Core.Data;

public class StoreCategoryDto
{
    public string StoreName { get; set; } = "";
    public string StoreCategoryId { get; set; } = "";
    public string StoreCategoryName { get; set; } = "";
    public string? ParentStoreCategoryId { get; set; }
}
