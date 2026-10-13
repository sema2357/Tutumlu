using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using PriceAggregator.Core.Data;
using PriceAggregator.Core.Helpers;

namespace PriceAggregator.Core.Categorization;

public static class CategoryResolver
{
    public class CategoryTaxonomyNode
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public int? ParentCategoryId { get; init; }
        public int? SectorId { get; init; }
        public int Depth { get; init; }
        public List<string> PathNames { get; init; } = [];
        public string NormalizedName { get; init; } = string.Empty;
        public HashSet<string> NameTokens { get; init; } = [];
        public HashSet<string> AncestorTokens { get; init; } = [];
        public HashSet<string> PathTokens { get; init; } = [];
    }

    private record ShopCategoryNode(long Id, string ExternalId, int Depth);

    public static readonly HashSet<string> IgnoredBreadcrumbRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ana Sayfa", "Anasayfa", "Kategoriler", "Tüm Kategoriler", "Tum Kategoriler",
        "Ürünler", "Urunler", "Tüm Ürünler", "Tum Urunler", "Market", "Home", "Başlangıç", "Baslangic", "Main"
    };

    private static List<CategoryTaxonomyNode>? _cachedTaxonomy;
    private static DateTime _taxonomyCacheTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<string, int> _shopIdCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(int ShopId, string VendorExternalId), int> _quickMappingCache = new();

    public static void InvalidateCache()
    {
        _cachedTaxonomy = null;
        _taxonomyCacheTime = DateTime.MinValue;
        _shopIdCache.Clear();
        _quickMappingCache.Clear();
    }

    public static async Task<List<CategoryTaxonomyNode>> LoadTaxonomyAsync(SqlConnection db)
    {
        if (_cachedTaxonomy is not null && DateTime.UtcNow - _taxonomyCacheTime < CacheDuration)
            return _cachedTaxonomy;

        await _cacheLock.WaitAsync();
        try
        {
            if (_cachedTaxonomy is not null && DateTime.UtcNow - _taxonomyCacheTime < CacheDuration)
                return _cachedTaxonomy;

            return await LoadTaxonomyCoreAsync(db);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static async Task<List<CategoryTaxonomyNode>> LoadTaxonomyCoreAsync(SqlConnection db)
    {
        var rawCategories = (await db.QueryAsync<(int Id, string Name, string Slug, int? ParentCategoryId, int? SectorId)>(@"
            SELECT Id, Name, Slug, ParentCategoryId, SectorId 
            FROM Categories 
            WHERE IsActive = 1")).ToList();

        var byId = rawCategories.ToDictionary(c => c.Id);
        var nodes = new List<CategoryTaxonomyNode>(rawCategories.Count);

        foreach (var cat in rawCategories)
        {
            var path = new List<string>();
            var curr = cat;
            while (true)
            {
                path.Insert(0, curr.Name);
                if (curr.ParentCategoryId.HasValue && byId.TryGetValue(curr.ParentCategoryId.Value, out var parent))
                {
                    curr = parent;
                }
                else
                {
                    break;
                }
            }

            var nameTokens = TurkishTextHelper.Tokenize(cat.Name);
            var ancestorTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < path.Count - 1; i++)
            {
                foreach (var t in TurkishTextHelper.Tokenize(path[i]))
                {
                    ancestorTokens.Add(t);
                }
            }

            var pathTokens = new HashSet<string>(ancestorTokens, StringComparer.OrdinalIgnoreCase);
            foreach (var t in nameTokens)
            {
                pathTokens.Add(t);
            }

            nodes.Add(new CategoryTaxonomyNode
            {
                Id = cat.Id,
                Name = cat.Name,
                Slug = cat.Slug,
                ParentCategoryId = cat.ParentCategoryId,
                SectorId = cat.SectorId,
                Depth = path.Count - 1,
                PathNames = path,
                NormalizedName = TurkishTextHelper.NormalizeToAscii(cat.Name),
                NameTokens = nameTokens,
                AncestorTokens = ancestorTokens,
                PathTokens = pathTokens
            });
        }

        try
        {
            var existingMappings = (await db.QueryAsync<(int ShopId, string VendorExternalId, int CanonicalCategoryId)>(@"
                SELECT sc.ShopId, sc.VendorExternalId, cm.CanonicalCategoryId
                FROM dbo.CategoryMappings cm
                JOIN dbo.ShopCategories sc ON sc.Id = cm.ShopCategoryId
                WHERE cm.ConfidenceScore >= 0.75 AND cm.MappedBy <> 'AUTO_PENDING'")).ToList();

            foreach (var m in existingMappings)
            {
                _quickMappingCache[(m.ShopId, m.VendorExternalId)] = m.CanonicalCategoryId;
            }
        }
        catch (SqlException)
        {
        }

        _cachedTaxonomy = nodes;
        _taxonomyCacheTime = DateTime.UtcNow;
        return nodes;
    }

    private static async Task<int> GetShopIdAsync(SqlConnection db, string source)
    {
        var key = source.Trim();
        if (_shopIdCache.TryGetValue(key, out var cachedId)) return cachedId;

        var id = await db.QuerySingleOrDefaultAsync<int?>(
            "SELECT Id FROM Sellers WHERE LOWER(Name) = LOWER(@Name)", new { Name = key });

        if (!id.HasValue)
        {
            try
            {
                id = await db.QuerySingleAsync<int>(
                    "INSERT INTO Sellers (Name) OUTPUT INSERTED.Id VALUES (@Name)", new { Name = key });
            }
            catch (SqlException)
            {
                id = await db.QuerySingleAsync<int>(
                    "SELECT Id FROM Sellers WHERE LOWER(Name) = LOWER(@Name)", new { Name = key });
            }
        }

        _shopIdCache[key] = id.Value;
        return id.Value;
    }

    private static string BuildVendorExternalId(string source, string normalizedPath, string? storeCategoryId, bool isLeaf)
    {
        if (isLeaf && !string.IsNullOrWhiteSpace(storeCategoryId))
        {
            var rawId = storeCategoryId.Trim();
            return rawId.Length <= 100 ? rawId : rawId[..100];
        }

        var fullKey = $"{source.ToLowerInvariant()}:{normalizedPath}";
        if (fullKey.Length <= 100) return fullKey;

        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(fullKey));
        var hashHex = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
        return $"{fullKey[..(100 - 17)]}_{hashHex}";
    }

    public static async Task<int?> ResolveAsync(SqlConnection db, ProductDto product, string source)
    {
        var breadcrumbs = GetBreadcrumbChain(product);
        var taxonomy = await LoadTaxonomyAsync(db);

        if (breadcrumbs.Count == 0)
        {
            return ResolveByTitleFallback(taxonomy, product.Title);
        }

        int shopId = await GetShopIdAsync(db, source);

        var normalizedLeafPath = string.Join("/", breadcrumbs.Select(b => TurkishTextHelper.GenerateSlug(b)));
        if (normalizedLeafPath.Length > 850) normalizedLeafPath = normalizedLeafPath[..850];

        var leafExternalId = BuildVendorExternalId(source, normalizedLeafPath, product.StoreCategoryId, isLeaf: true);

        if (_quickMappingCache.TryGetValue((shopId, leafExternalId), out var cachedCanonicalId))
        {
            return cachedCanonicalId;
        }

        var shopNodes = await SyncVendorTreeAsync(db, shopId, breadcrumbs, product.StoreCategoryId, source);
        if (shopNodes.Count == 0) return null;

        var leafNode = shopNodes[^1];

        var existingCanonicalId = await GetExistingMappingAsync(db, leafNode.Id);
        if (existingCanonicalId.HasValue)
        {
            _quickMappingCache[(shopId, leafNode.ExternalId)] = existingCanonicalId.Value;
            return existingCanonicalId.Value;
        }

        var leafBreadcrumb = breadcrumbs[^1];
        var ancestorBreadcrumbs = breadcrumbs.Take(breadcrumbs.Count - 1).ToList();

        var (bestMatch, confidence) = EvaluateCandidate(taxonomy, leafBreadcrumb, ancestorBreadcrumbs);

        if (bestMatch is not null && confidence >= 0.75)
        {
            await SaveMappingAsync(db, leafNode.Id, bestMatch.Id, confidence, "HEURISTIC");
            _quickMappingCache[(shopId, leafNode.ExternalId)] = bestMatch.Id;
            return bestMatch.Id;
        }

        int adoptedCanonicalId = await AutoAdoptCategoryAsync(
            db, taxonomy, shopId, shopNodes, breadcrumbs, confidence);

        _quickMappingCache[(shopId, leafNode.ExternalId)] = adoptedCanonicalId;
        return adoptedCanonicalId;
    }

    public static async Task<List<int?>> ResolveBatchAsync(SqlConnection db, List<(ProductDto Product, string Source)> items)
    {
        var results = new List<int?>(items.Count);
        foreach (var (product, source) in items)
        {
            results.Add(await ResolveAsync(db, product, source));
        }
        return results;
    }

    private static List<string> GetBreadcrumbChain(ProductDto product)
    {
        var list = new List<string>();

        if (product.CategoryPath != null && product.CategoryPath.Count > 0)
        {
            list.AddRange(product.CategoryPath.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(product.RootCategoryName))
                list.Add(product.RootCategoryName.Trim());

            if (!string.IsNullOrWhiteSpace(product.ParentCategoryName) &&
                !list.Any(x => x.Equals(product.ParentCategoryName.Trim(), StringComparison.OrdinalIgnoreCase)))
                list.Add(product.ParentCategoryName.Trim());

            if (!string.IsNullOrWhiteSpace(product.CategoryName) &&
                !list.Any(x => x.Equals(product.CategoryName.Trim(), StringComparison.OrdinalIgnoreCase)))
                list.Add(product.CategoryName.Trim());
        }

        var filtered = list.Where(b => !IgnoredBreadcrumbRoots.Contains(b.Trim())).ToList();

        var cleaned = new List<string>(filtered.Count);
        for (int i = 0; i < filtered.Count; i++)
        {
            var item = filtered[i].Trim();
            if (string.IsNullOrWhiteSpace(item)) continue;

            if (cleaned.Count > 0 && item.Equals(cleaned[^1], StringComparison.OrdinalIgnoreCase))
                continue;

            cleaned.Add(item);
        }

        return cleaned;
    }

    private static async Task<List<ShopCategoryNode>> SyncVendorTreeAsync(
        SqlConnection db, int shopId, List<string> breadcrumbs, string? storeCategoryId, string source)
    {
        var result = new List<ShopCategoryNode>(breadcrumbs.Count);
        long? currentParentId = null;
        string? currentParentVendorId = null;

        for (int i = 0; i < breadcrumbs.Count; i++)
        {
            var segment = breadcrumbs[i].Trim();
            bool isLeaf = (i == breadcrumbs.Count - 1);

            var rawPath = string.Join(" > ", breadcrumbs.Take(i + 1));
            var normalizedPath = string.Join("/", breadcrumbs.Take(i + 1).Select(b => TurkishTextHelper.GenerateSlug(b)));
            if (normalizedPath.Length > 850) normalizedPath = normalizedPath[..850];
            if (rawPath.Length > 1000) rawPath = rawPath[..1000];

            string vendorExternalId = BuildVendorExternalId(source, normalizedPath, storeCategoryId, isLeaf);

            var existing = await db.QuerySingleOrDefaultAsync<(long Id, long? ParentId)>(
                "SELECT Id, ParentId FROM dbo.ShopCategories WHERE ShopId = @ShopId AND VendorExternalId = @VendorExternalId",
                new { ShopId = shopId, VendorExternalId = vendorExternalId });

            long nodeId;
            if (existing.Id > 0)
            {
                nodeId = existing.Id;
                await db.ExecuteAsync(
                    @"UPDATE dbo.ShopCategories 
                      SET LastSeenAt = GETUTCDATE(),
                          ParentId = COALESCE(ParentId, @ParentId),
                          ParentVendorId = COALESCE(ParentVendorId, @ParentVendorId)
                      WHERE Id = @Id",
                    new { Id = nodeId, ParentId = currentParentId, ParentVendorId = currentParentVendorId });
            }
            else
            {
                try
                {
                    nodeId = await db.QuerySingleAsync<long>(@"
                        INSERT INTO dbo.ShopCategories (ShopId, VendorExternalId, ParentVendorId, ParentId, Name, RawPath, NormalizedPath, Depth, LastSeenAt, CreatedAt)
                        OUTPUT INSERTED.Id
                        VALUES (@ShopId, @VendorExternalId, @ParentVendorId, @ParentId, @Name, @RawPath, @NormalizedPath, @Depth, GETUTCDATE(), GETUTCDATE())",
                        new
                        {
                            ShopId = shopId,
                            VendorExternalId = vendorExternalId,
                            ParentVendorId = currentParentVendorId,
                            ParentId = currentParentId,
                            Name = segment,
                            RawPath = rawPath,
                            NormalizedPath = normalizedPath,
                            Depth = i
                        });
                }
                catch (SqlException)
                {
                    nodeId = await db.QuerySingleAsync<long>(
                        "SELECT Id FROM dbo.ShopCategories WHERE ShopId = @ShopId AND VendorExternalId = @VendorExternalId",
                        new { ShopId = shopId, VendorExternalId = vendorExternalId });
                }
            }

            result.Add(new ShopCategoryNode(nodeId, vendorExternalId, i));
            currentParentId = nodeId;
            currentParentVendorId = vendorExternalId;
        }

        return result;
    }

    private static async Task<int?> GetExistingMappingAsync(SqlConnection db, long shopCategoryId)
    {
        return await db.QuerySingleOrDefaultAsync<int?>(
            "SELECT CanonicalCategoryId FROM dbo.CategoryMappings WHERE ShopCategoryId = @ShopCategoryId AND ConfidenceScore >= 0.75 AND MappedBy <> 'AUTO_PENDING'",
            new { ShopCategoryId = shopCategoryId });
    }

    public static (CategoryTaxonomyNode? BestNode, double Confidence) EvaluateCandidate(
        List<CategoryTaxonomyNode> taxonomy,
        string leafName,
        List<string> ancestorNames)
    {
        var leafNorm = TurkishTextHelper.NormalizeToAscii(leafName);
        var leafTokens = TurkishTextHelper.Tokenize(leafName);
        if (leafTokens.Count == 0) return (null, 0.0);

        var vendorAncestorTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anc in ancestorNames)
        {
            foreach (var t in TurkishTextHelper.Tokenize(anc))
            {
                vendorAncestorTokens.Add(t);
            }
        }

        CategoryTaxonomyNode? bestNode = null;
        double bestConfidence = 0.0;

        var inputHead = TurkishTextHelper.GetHeadNoun(leafName);

        foreach (var node in taxonomy)
        {
            double leafScore;
            if (string.Equals(leafNorm, node.NormalizedName, StringComparison.OrdinalIgnoreCase))
            {
                leafScore = 1.0;
            }
            else if (TurkishTextHelper.IsStemMatch(leafNorm, node.NormalizedName))
            {
                leafScore = 0.95;
            }
            else
            {
                var overlap = TurkishTextHelper.ComputeTokenOverlap(leafTokens, node.NameTokens);
                if (overlap <= 0.0) continue;
                leafScore = overlap >= 0.99 ? 0.95 : overlap;

                // Dynamic Head Noun & Modifier Check:
                // 1. If leaf is a single general word ("Kahve") and node is a specialized compound ("Soğuk Kahveler"),
                // penalize to prevent general categories matching specialized children.
                if (leafTokens.Count == 1 && node.NameTokens.Count >= 2)
                {
                    leafScore *= 0.50;
                }
                // 2. If both the leaf and candidate are compound phrases (>= 2 tokens):
                // In Turkish grammar, an endocentric compound [Modifier] [Head Noun] is fundamentally defined by its Head Noun.
                else if (inputHead != null && leafTokens.Count >= 2 && node.NameTokens.Count >= 2)
                {
                    var nodeHead = TurkishTextHelper.GetHeadNoun(node.Name);
                    if (nodeHead != null)
                    {
                        bool exactHeadMatch = TurkishTextHelper.IsStemMatch(inputHead, nodeHead);
                        if (!exactHeadMatch)
                        {
                            // Reversed head conflict: leaf is "X Y" (head Y) while candidate is "Y X" (head X)
                            // e.g. "Kahve Filtresi" (head filtre) vs "Filtre Kahveler" (head kahve).
                            // A coffee filter is a filter, NOT coffee. Heavy penalty!
                            var leafModifier = TurkishTextHelper.GetHeadNoun(string.Join(" ", leafTokens.Take(leafTokens.Count - 1)));
                            bool isReversed = leafModifier != null && TurkishTextHelper.IsStemMatch(leafModifier, nodeHead);

                            if (isReversed)
                            {
                                leafScore *= 0.10;
                                if (leafScore < 0.15) continue;
                            }
                            else
                            {
                                // Check if candidate includes inputHead as a composite entity
                                // e.g. "Kahve Filtresi" [filtre] matching "Filtre Kahve Kağıdı"
                                bool containsHead = node.NameTokens.Any(t => TurkishTextHelper.IsStemMatch(t, inputHead));
                                if (containsHead)
                                {
                                    leafScore *= 0.85;
                                }
                                else
                                {
                                    leafScore *= 0.15;
                                    if (leafScore < 0.15) continue;
                                }
                            }
                        }
                    }
                }
            }

            double ancestorScore;
            bool vendorHasAncestors = vendorAncestorTokens.Count > 0;
            bool nodeHasAncestors = node.AncestorTokens.Count > 0;

            if (!vendorHasAncestors && !nodeHasAncestors)
            {
                ancestorScore = 1.0;
            }
            else if (vendorHasAncestors && nodeHasAncestors)
            {
                double immediateParentScore = 0.0;
                if (ancestorNames.Count > 0 && node.PathNames.Count >= 2)
                {
                    var vendorImmediateParent = ancestorNames[^1];
                    var canonicalImmediateParent = node.PathNames[^2];
                    if (TurkishTextHelper.IsStemMatch(vendorImmediateParent, canonicalImmediateParent) ||
                        TurkishTextHelper.ComputeTokenOverlap(
                            TurkishTextHelper.Tokenize(vendorImmediateParent),
                            TurkishTextHelper.Tokenize(canonicalImmediateParent)) >= 0.5)
                    {
                        immediateParentScore = 1.0;
                    }
                }

                int matches = 0;
                foreach (var v in vendorAncestorTokens)
                {
                    if (node.AncestorTokens.Any(a => TurkishTextHelper.IsStemMatch(v, a)))
                        matches++;
                }
                double allAncestorScore = (double)matches / vendorAncestorTokens.Count;

                ancestorScore = immediateParentScore > 0
                    ? (0.7 * immediateParentScore + 0.3 * allAncestorScore)
                    : (allAncestorScore * 0.7);

                // If vendor has ancestors, but candidate has ZERO ancestor overlap with vendor ancestors:
                if (matches == 0 && immediateParentScore == 0.0)
                {
                    ancestorScore = 0.0;
                }
            }
            else if (!vendorHasAncestors && nodeHasAncestors)
            {
                ancestorScore = 0.5;
            }
            else
            {
                ancestorScore = 0.0;
            }

            // Dynamic Leaf Dominance:
            // When leaf matches fully (exact or full stem match), the leaf represents the true product entity.
            // Minor department hierarchy differences should not drag down an exact leaf match.
            double confidence;
            if (leafScore >= 0.90)
            {
                confidence = (0.80 * leafScore) + (0.20 * ancestorScore);
            }
            else
            {
                confidence = (0.65 * leafScore) + (0.35 * ancestorScore);
            }

            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestNode = node;
            }
        }

        return (bestNode, bestConfidence);
    }

    private static async Task<int> AutoAdoptCategoryAsync(
        SqlConnection db,
        List<CategoryTaxonomyNode> taxonomy,
        int shopId,
        List<ShopCategoryNode> shopNodes,
        List<string> breadcrumbs,
        double leafConfidence)
    {
        await _cacheLock.WaitAsync();
        try
        {
            var leafNode = shopNodes[^1];
            var leafBreadcrumb = breadcrumbs[^1];
            var leafNorm = TurkishTextHelper.NormalizeToAscii(leafBreadcrumb);
            var leafTokens = TurkishTextHelper.Tokenize(leafBreadcrumb);

            CategoryTaxonomyNode? canonicalParent = null;

            for (int i = breadcrumbs.Count - 2; i >= 0; i--)
            {
                var ancShopNode = shopNodes[i];
                var ancMapping = await GetExistingMappingAsync(db, ancShopNode.Id);

                if (ancMapping.HasValue)
                {
                    canonicalParent = taxonomy.FirstOrDefault(n => n.Id == ancMapping.Value);
                    if (canonicalParent is not null) break;
                }

                var (ancMatch, ancConfidence) = EvaluateCandidate(
                    taxonomy,
                    breadcrumbs[i],
                    breadcrumbs.Take(i).ToList());

                if (ancMatch is not null && ancConfidence >= 0.75)
                {
                    await SaveMappingAsync(db, ancShopNode.Id, ancMatch.Id, ancConfidence, "HEURISTIC");
                    canonicalParent = ancMatch;
                    break;
                }
            }

            if (canonicalParent is null)
            {
                var rootBreadcrumb = breadcrumbs[0];
                var rootTokens = TurkishTextHelper.Tokenize(rootBreadcrumb);
                var rootCategories = taxonomy.Where(n => !n.ParentCategoryId.HasValue).ToList();

                canonicalParent = rootCategories
                    .OrderByDescending(r => TurkishTextHelper.ComputeTokenOverlap(rootTokens, r.NameTokens))
                    .FirstOrDefault();

                if (canonicalParent is null || TurkishTextHelper.ComputeTokenOverlap(rootTokens, canonicalParent.NameTokens) <= 0.0)
                {
                    var anyMatch = taxonomy
                        .Where(n => n.ParentCategoryId.HasValue)
                        .OrderByDescending(r => TurkishTextHelper.ComputeTokenOverlap(leafTokens, r.NameTokens))
                        .FirstOrDefault();

                    if (anyMatch != null && TurkishTextHelper.ComputeTokenOverlap(leafTokens, anyMatch.NameTokens) > 0.3)
                    {
                        canonicalParent = anyMatch;
                    }
                    else
                    {
                        canonicalParent = rootCategories.FirstOrDefault();
                    }
                }

                if (canonicalParent is null) return 0;
            }

            var existingChild = taxonomy.FirstOrDefault(n =>
                n.ParentCategoryId == canonicalParent.Id &&
                (string.Equals(n.NormalizedName, leafNorm, StringComparison.OrdinalIgnoreCase) ||
                 TurkishTextHelper.IsStemMatch(n.NormalizedName, leafNorm) ||
                 TurkishTextHelper.ComputeTokenOverlap(leafTokens, n.NameTokens) >= 0.8));

            int targetCanonicalId;
            if (existingChild is not null)
            {
                targetCanonicalId = existingChild.Id;
            }
            else
            {
                // In enterprise B2B taxonomy, never insert vendor categories into dbo.Categories.
                // 1. Look for matching children under the resolved canonical parent first!
                var leafHead = TurkishTextHelper.GetHeadNoun(leafBreadcrumb);

                var bestChild = taxonomy
                    .Where(n => n.ParentCategoryId == canonicalParent.Id)
                    .Select(n =>
                    {
                        double overlap = TurkishTextHelper.ComputeTokenOverlap(leafTokens, n.NameTokens);
                        bool isStem = TurkishTextHelper.IsStemMatch(n.NormalizedName, leafNorm);
                        double score = isStem ? 0.95 : overlap;

                        if (leafHead != null && leafTokens.Count >= 2 && n.NameTokens.Count >= 2)
                        {
                            var nHead = TurkishTextHelper.GetHeadNoun(n.Name);
                            if (nHead != null && !TurkishTextHelper.IsStemMatch(leafHead, nHead))
                            {
                                var leafMod = TurkishTextHelper.GetHeadNoun(string.Join(" ", leafTokens.Take(leafTokens.Count - 1)));
                                bool isRev = leafMod != null && TurkishTextHelper.IsStemMatch(leafMod, nHead);
                                if (isRev) score *= 0.10;
                                else if (n.NameTokens.Any(t => TurkishTextHelper.IsStemMatch(t, leafHead))) score *= 0.85;
                                else score *= 0.15;
                            }
                        }

                        return new { Node = n, Score = score };
                    })
                    .Where(x => x.Score >= 0.35)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                if (bestChild is not null)
                {
                    targetCanonicalId = bestChild.Node.Id;
                }
                else
                {
                    // Fall back to canonicalParent.Id itself. Never jump across to an unrelated parent branch!
                    targetCanonicalId = canonicalParent.Id;
                }
            }

            await SaveMappingAsync(db, leafNode.Id, targetCanonicalId, leafConfidence, "HEURISTIC", "Mapped to canonical taxonomy");
            return targetCanonicalId;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static async Task SaveMappingAsync(
        SqlConnection db, long shopCategoryId, int canonicalCategoryId, double confidence, string mappedBy, string? notes = null)
    {
        try
        {
            await db.ExecuteAsync(@"
                IF EXISTS (SELECT 1 FROM dbo.CategoryMappings WHERE ShopCategoryId = @ShopCategoryId)
                BEGIN
                    UPDATE dbo.CategoryMappings
                    SET CanonicalCategoryId = @CanonicalCategoryId,
                        ConfidenceScore = @ConfidenceScore,
                        MappedBy = @MappedBy,
                        Notes = @Notes,
                        UpdatedAt = GETUTCDATE()
                    WHERE ShopCategoryId = @ShopCategoryId
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.CategoryMappings (ShopCategoryId, CanonicalCategoryId, ConfidenceScore, MappedBy, Notes, CreatedAt, UpdatedAt)
                    VALUES (@ShopCategoryId, @CanonicalCategoryId, @ConfidenceScore, @MappedBy, @Notes, GETUTCDATE(), GETUTCDATE())
                END",
                new
                {
                    ShopCategoryId = shopCategoryId,
                    CanonicalCategoryId = canonicalCategoryId,
                    ConfidenceScore = Math.Round((decimal)confidence, 4),
                    MappedBy = mappedBy,
                    Notes = notes
                });
        }
        catch (SqlException)
        {
        }
    }

    public static int? ResolveByTitleFallback(List<CategoryTaxonomyNode> taxonomy, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var titleTokens = TurkishTextHelper.Tokenize(title);
        if (titleTokens.Count == 0) return null;

        CategoryTaxonomyNode? best = null;
        double bestScore = 0.0;

        foreach (var node in taxonomy.Where(n => n.Depth >= 1))
        {
            if (node.NameTokens.Count == 0) continue;

            int matched = 0;
            foreach (var catToken in node.NameTokens)
            {
                if (titleTokens.Any(t => TurkishTextHelper.IsStemMatch(t, catToken)))
                    matched++;
            }

            if (matched == node.NameTokens.Count)
            {
                double score = node.NameTokens.Count * 10.0 + node.Depth;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }
        }

        return best?.Id;
    }

    public static async Task<int> AlignUnmappedShopCategoriesAsync(SqlConnection db)
    {
        var taxonomy = await LoadTaxonomyAsync(db);

        // Fetch unmapped shop categories that have raw paths
        var unmapped = (await db.QueryAsync<(long Id, int ShopId, string Name, string RawPath, int Depth)>(@"
            SELECT sc.Id, sc.ShopId, sc.Name, sc.RawPath, sc.Depth
            FROM dbo.ShopCategories sc
            LEFT JOIN dbo.CategoryMappings cm ON sc.Id = cm.ShopCategoryId
            WHERE (cm.Id IS NULL OR cm.ConfidenceScore < 0.75 OR cm.MappedBy = 'AUTO_PENDING') AND sc.RawPath IS NOT NULL")).ToList();

        if (unmapped.Count == 0) return 0;

        int mappedCount = 0;
        foreach (var sc in unmapped)
        {
            var segments = sc.RawPath.Split(new[] { " > ", " » ", ">", "/" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !IgnoredBreadcrumbRoots.Contains(s))
                .ToList();

            if (segments.Count == 0) continue;

            var leaf = segments[^1];
            var ancestors = segments.Take(segments.Count - 1).ToList();

            var (bestMatch, confidence) = EvaluateCandidate(taxonomy, leaf, ancestors);

            if (bestMatch != null && confidence >= 0.65)
            {
                await SaveMappingAsync(db, sc.Id, bestMatch.Id, confidence, "TREE_ALIGN", "Batch Category Tree Alignment");
                mappedCount++;

                // Propagate canonical category to all existing products from this category path
                try
                {
                    await db.ExecuteAsync(@"
                        UPDATE p
                        SET p.CategoryId = @CanonicalCategoryId
                        FROM dbo.Products p
                        JOIN dbo.Offers o ON p.Id = o.ProductId
                        JOIN dbo.SellerProducts sp ON o.SellerProductId = sp.Id
                        WHERE sp.OriginalCategory = @RawPath AND p.CategoryId <> @CanonicalCategoryId",
                        new { CanonicalCategoryId = bestMatch.Id, RawPath = sc.RawPath });
                }
                catch (SqlException)
                {
                }
            }
            else
            {
                // Purge low-confidence AUTO_PENDING mappings so they cannot poison future categorizations
                await db.ExecuteAsync(@"
                    DELETE FROM dbo.CategoryMappings
                    WHERE ShopCategoryId = @ShopCategoryId AND MappedBy = 'AUTO_PENDING'",
                    new { ShopCategoryId = sc.Id });
            }
        }

        // Universal Product Sync: Ensure ALL products whose vendor category has a verified mapping (>= 0.65)
        // are synchronized to their true canonical CategoryId, healing any historical forcedCategoryId misplacements!
        try
        {
            await db.ExecuteAsync(@"
                UPDATE p
                SET p.CategoryId = cm.CanonicalCategoryId
                FROM dbo.Products p
                JOIN dbo.Offers o ON p.Id = o.ProductId
                JOIN dbo.SellerProducts sp ON o.SellerProductId = sp.Id
                JOIN dbo.ShopCategories sc ON sp.OriginalCategory = sc.RawPath
                JOIN dbo.CategoryMappings cm ON sc.Id = cm.ShopCategoryId
                WHERE cm.ConfidenceScore >= 0.65 
                  AND cm.MappedBy <> 'AUTO_PENDING'
                  AND p.CategoryId <> cm.CanonicalCategoryId;");
        }
        catch (SqlException)
        {
        }

        InvalidateCache();
        return mappedCount;
    }
}
