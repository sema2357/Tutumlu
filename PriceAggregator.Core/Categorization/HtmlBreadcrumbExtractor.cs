using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using PriceAggregator.Core.Helpers;

namespace PriceAggregator.Core.Categorization;

public static class HtmlBreadcrumbExtractor
{
    private static readonly Regex JsonLdRegex = new(
        @"<script[^>]*?type=""application/ld\+json""[^>]*?>(.*?)</script>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex BreadcrumbItemRegex = new(
        @"<(?:div|li|span|a)[^>]*?class=""[^""]*?breadcrumb[-_]*item(?:\s+[^""]*)?""[^>]*?title=""([^""]+)""[^>]*?>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex BreadcrumbBlockRegex = new(
        @"<(?:ol|ul|div|nav)[^>]*?class=""[^""]*?breadcrumb[^""]*?""[^>]*?>(.*?)</(?:ol|ul|div|nav)>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex AriaBreadcrumbRegex = new(
        @"<(?:ol|ul|div|nav)[^>]*?aria-label=""breadcrumb""[^>]*?>(.*?)</(?:ol|ul|div|nav)>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex DropdownMenuRegex = new(
        @"<div[^>]*?class=""[^""]*?(?:dropdown-menu|dropdown)[^""]*?""[^>]*?>.*?</div>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex LiTagRegex = new(
        @"<li[^>]*?>(.*?)</li>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex AnchorTagRegex = new(
        @"<a[^>]*?>(.*?)</a>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    public static List<string> Extract(string? html, string? productTitle = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];

        var breadcrumbs = ExtractFromJsonLd(html);

        if (breadcrumbs.Count == 0)
        {
            breadcrumbs = ExtractFromHtmlMarkup(html);
        }

        if (breadcrumbs.Count >= 2 && !string.IsNullOrWhiteSpace(productTitle))
        {
            var last = breadcrumbs[^1];
            var trimmedTitle = productTitle.Trim();

            if (string.Equals(last, trimmedTitle, StringComparison.OrdinalIgnoreCase) ||
                TurkishTextHelper.IsStemMatch(last, trimmedTitle) ||
                (last.Length >= 15 && trimmedTitle.Contains(last, StringComparison.OrdinalIgnoreCase)) ||
                (trimmedTitle.Length >= 15 && last.Contains(trimmedTitle, StringComparison.OrdinalIgnoreCase)))
            {
                breadcrumbs.RemoveAt(breadcrumbs.Count - 1);
            }
        }

        return breadcrumbs;
    }

    public static (string? RootCategory, string? ParentCategory, string? LeafCategory) ToHierarchy(List<string> breadcrumbs)
    {
        if (breadcrumbs == null || breadcrumbs.Count == 0)
            return (null, null, null);

        var cleaned = breadcrumbs
            .Select(CleanBreadcrumbText)
            .Where(b => !string.IsNullOrWhiteSpace(b) && !CategoryResolver.IgnoredBreadcrumbRoots.Contains(b))
            .ToList();

        if (cleaned.Count == 0)
            return (null, null, null);

        if (cleaned.Count == 1)
            return (null, null, cleaned[0]);

        if (cleaned.Count == 2)
            return (cleaned[0], null, cleaned[1]);

        if (cleaned.Count == 3)
            return (cleaned[0], cleaned[1], cleaned[2]);

        return (cleaned[0], cleaned[^2], cleaned[^1]);
    }

    private static List<string> ExtractFromJsonLd(string html)
    {
        var scriptMatches = JsonLdRegex.Matches(html);

        foreach (Match m in scriptMatches)
        {
            try
            {
                var json = m.Groups[1].Value.Trim();
                if (!json.Contains("BreadcrumbList", StringComparison.OrdinalIgnoreCase)) continue;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var list = ParseBreadcrumbElement(root);
                if (list.Count > 0) return list;
            }
            catch
            {
            }
        }

        return [];
    }

    private static List<string> ParseBreadcrumbElement(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in elem.EnumerateArray())
            {
                var res = ParseBreadcrumbElement(child);
                if (res.Count > 0) return res;
            }
            return [];
        }

        if (elem.ValueKind == JsonValueKind.Object)
        {
            bool isBreadcrumb = false;
            if (elem.TryGetProperty("@type", out var typeProp))
            {
                if (typeProp.ValueKind == JsonValueKind.String && typeProp.GetString() == "BreadcrumbList")
                    isBreadcrumb = true;
                else if (typeProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in typeProp.EnumerateArray())
                    {
                        if (t.GetString() == "BreadcrumbList") { isBreadcrumb = true; break; }
                    }
                }
            }

            if (isBreadcrumb && elem.TryGetProperty("itemListElement", out var listProp) && listProp.ValueKind == JsonValueKind.Array)
            {
                var items = new List<(int Position, string Name)>();
                int defaultPos = 1;
                foreach (var item in listProp.EnumerateArray())
                {
                    string? name = null;
                    int pos = defaultPos++;

                    if (item.TryGetProperty("position", out var posProp))
                    {
                        if (posProp.ValueKind == JsonValueKind.Number) pos = posProp.GetInt32();
                        else if (posProp.ValueKind == JsonValueKind.String && int.TryParse(posProp.GetString(), out var p)) pos = p;
                    }

                    if (item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    {
                        name = n.GetString();
                    }
                    else if (item.TryGetProperty("item", out var itemObj) && itemObj.ValueKind == JsonValueKind.Object)
                    {
                        if (itemObj.TryGetProperty("name", out var iname) && iname.ValueKind == JsonValueKind.String)
                            name = iname.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var cleaned = CleanBreadcrumbText(name);
                        if (!string.IsNullOrWhiteSpace(cleaned) && !CategoryResolver.IgnoredBreadcrumbRoots.Contains(cleaned))
                        {
                            items.Add((pos, cleaned));
                        }
                    }
                }

                if (items.Count > 0)
                {
                    return items.OrderBy(x => x.Position).Select(x => x.Name).ToList();
                }
            }

            foreach (var prop in elem.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var res = ParseBreadcrumbElement(prop.Value);
                    if (res.Count > 0) return res;
                }
            }
        }

        return [];
    }

    private static List<string> ExtractFromHtmlMarkup(string html)
    {
        var results = new List<string>();

        var itemMatches = BreadcrumbItemRegex.Matches(html);
        if (itemMatches.Count > 0)
        {
            foreach (Match m in itemMatches)
            {
                var fullTag = m.Value;
                if (fullTag.Contains("item--last", StringComparison.OrdinalIgnoreCase) ||
                    fullTag.Contains("item-last", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = CleanBreadcrumbText(m.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(text) && !CategoryResolver.IgnoredBreadcrumbRoots.Contains(text))
                {
                    results.Add(text);
                }
            }

            if (results.Count > 0) return results;
        }

        var breadcrumbBlockMatch = BreadcrumbBlockRegex.Match(html);
        string block = breadcrumbBlockMatch.Success ? breadcrumbBlockMatch.Groups[1].Value : "";

        if (string.IsNullOrWhiteSpace(block))
        {
            var ariaMatch = AriaBreadcrumbRegex.Match(html);
            if (ariaMatch.Success) block = ariaMatch.Groups[1].Value;
        }

        if (string.IsNullOrWhiteSpace(block)) return results;

        block = DropdownMenuRegex.Replace(block, "");

        var liMatches = LiTagRegex.Matches(block);
        if (liMatches.Count > 0)
        {
            foreach (Match li in liMatches)
            {
                var text = CleanBreadcrumbText(li.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(text) && !CategoryResolver.IgnoredBreadcrumbRoots.Contains(text))
                {
                    results.Add(text);
                }
            }
        }
        else
        {
            var linkMatches = AnchorTagRegex.Matches(block);
            foreach (Match a in linkMatches)
            {
                var text = CleanBreadcrumbText(a.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(text) && !CategoryResolver.IgnoredBreadcrumbRoots.Contains(text))
                {
                    results.Add(text);
                }
            }
        }

        return results;
    }

    public static string CleanBreadcrumbText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var noHtml = HtmlTagRegex.Replace(raw, " ");
        var decoded = WebUtility.HtmlDecode(noHtml);
        var normalized = WhitespaceRegex.Replace(decoded, " ").Trim();
        return normalized;
    }
}
