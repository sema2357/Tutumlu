using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Categorization;

public static class TaxonomySanitizer
{
    private class CatInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public int? ParentCategoryId { get; set; }
        public int? SectorId { get; set; }
        public string NormalizedName { get; set; } = "";
        public HashSet<string> Tokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<int> RemapAndPurgeRogueCategoriesAsync(string connectionString)
    {
        using var db = new SqlConnection(connectionString);
        await db.OpenAsync();

        var canonicalRaw = (await db.QueryAsync<CatInfo>(
            "SELECT Id, Name, Slug, ParentCategoryId, SectorId FROM Categories WHERE Id <= 496")).ToList();

        foreach (var c in canonicalRaw)
        {
            c.NormalizedName = TurkishTextHelper.NormalizeToAscii(c.Name);
            c.Tokens = TurkishTextHelper.Tokenize(c.Name);
        }

        var rogueRaw = (await db.QueryAsync<CatInfo>(
            "SELECT Id, Name, Slug, ParentCategoryId, SectorId FROM Categories WHERE Id > 496")).ToList();

        if (rogueRaw.Count == 0)
        {
            Logger.Log("[TaxonomySanitizer] Veritabanında rogue kategori bulunamadı (Tüm kategoriler canonical).");
            return 0;
        }

        Logger.Log($"[TaxonomySanitizer] Toplam {rogueRaw.Count} adet rogue kategori tespit edildi. Eşleştirme başlatılıyor...");

        var remappings = new Dictionary<int, int>();

        foreach (var rogue in rogueRaw)
        {
            var rogueNorm = TurkishTextHelper.NormalizeToAscii(rogue.Name);
            var rogueTokens = TurkishTextHelper.Tokenize(rogue.Name);

            // 1. Exact or stem match in same sector
            var sameSector = canonicalRaw.Where(c => c.SectorId == rogue.SectorId || c.Id == rogue.SectorId).ToList();

            var exactMatch = sameSector.FirstOrDefault(c => 
                string.Equals(c.NormalizedName, rogueNorm, StringComparison.OrdinalIgnoreCase) ||
                TurkishTextHelper.IsStemMatch(c.NormalizedName, rogueNorm));

            if (exactMatch != null)
            {
                remappings[rogue.Id] = exactMatch.Id;
                continue;
            }

            // 2. Best token overlap in same sector
            var bestSectorMatch = sameSector
                .Select(c => new { Cat = c, Overlap = TurkishTextHelper.ComputeTokenOverlap(rogueTokens, c.Tokens) })
                .Where(x => x.Overlap >= 0.4)
                .OrderByDescending(x => x.Overlap)
                .FirstOrDefault();

            if (bestSectorMatch != null)
            {
                remappings[rogue.Id] = bestSectorMatch.Cat.Id;
                continue;
            }

            // 3. Best token overlap across entire canonical catalog
            var bestGlobalMatch = canonicalRaw
                .Select(c => new { Cat = c, Overlap = TurkishTextHelper.ComputeTokenOverlap(rogueTokens, c.Tokens) })
                .Where(x => x.Overlap >= 0.4)
                .OrderByDescending(x => x.Overlap)
                .FirstOrDefault();

            if (bestGlobalMatch != null)
            {
                remappings[rogue.Id] = bestGlobalMatch.Cat.Id;
                continue;
            }

            // 4. Fallback to valid canonical parent or sector
            if (rogue.ParentCategoryId.HasValue && rogue.ParentCategoryId.Value <= 496)
            {
                remappings[rogue.Id] = rogue.ParentCategoryId.Value;
            }
            else if (rogue.SectorId.HasValue && rogue.SectorId.Value <= 496)
            {
                remappings[rogue.Id] = rogue.SectorId.Value;
            }
            else
            {
                remappings[rogue.Id] = 1; // Default to Ofis Kırtasiye sector
            }
        }

        using var tran = db.BeginTransaction();
        try
        {
            int updatedProducts = 0;
            int updatedMappings = 0;

            foreach (var (rogueId, canonicalId) in remappings)
            {
                var prodRows = await db.ExecuteAsync(
                    "UPDATE Products SET CategoryId = @CanonicalId WHERE CategoryId = @RogueId",
                    new { CanonicalId = canonicalId, RogueId = rogueId },
                    tran);
                updatedProducts += prodRows;

                var mapRows = await db.ExecuteAsync(
                    "UPDATE CategoryMappings SET CanonicalCategoryId = @CanonicalId WHERE CanonicalCategoryId = @RogueId",
                    new { CanonicalId = canonicalId, RogueId = rogueId },
                    tran);
                updatedMappings += mapRows;
            }

            // Break self-referencing FK on Categories
            await db.ExecuteAsync("UPDATE Categories SET ParentCategoryId = NULL WHERE Id > 496", transaction: tran);

            // Delete rogue categories
            int deletedCategories = await db.ExecuteAsync("DELETE FROM Categories WHERE Id > 496", transaction: tran);

            tran.Commit();

            Logger.Log($"[TaxonomySanitizer] BAŞARILI: {updatedProducts} ürün ve {updatedMappings} eşleme canonical kategorilere aktarıldı. {deletedCategories} rogue kategori silindi.");
            return deletedCategories;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            Logger.Log($"[TaxonomySanitizer] Hata oluştu, geri alındı: {ex.Message}");
            throw;
        }
    }
}
