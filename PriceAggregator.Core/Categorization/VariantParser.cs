using System.Text.RegularExpressions;
using PriceAggregator.Core.Helpers;

namespace PriceAggregator.Core.Categorization;

public static class VariantParser
{
    private record VariantDefinition(string CanonicalName, string[] Keywords);

    // Kategoriye ve ürün tipine göre kapsamlı varyant kuralları
    private static readonly VariantDefinition[] VariantDictionary =
    [
        // ==========================================
        // 1. İÇECEKLER & SULAR (Tat, Aroma, Gaz, Çay/Kahve)
        // ==========================================
        new("Acılı", ["acili", "aci"]),
        new("Acısız", ["acisiz"]),
        new("Şeftali", ["seftali", "seftalili", "peach"]),
        new("Limon", ["limon", "limonlu", "lemon", "lime", "misket limonu", "misket limon"]),
        new("Çilek", ["cilek", "cilekli", "strawberry"]),
        new("Muz", ["muz", "muzlu", "banana"]),
        new("Karpuz", ["karpuz", "karpuzlu", "watermelon"]),
        new("Kavun", ["kavun", "kavunlu", "melon"]),
        new("Mango", ["mango", "mangolu"]),
        new("Ananas", ["ananas", "ananasli", "pineapple"]),
        new("Elma", ["elma", "elmali", "yesil elma", "kirmizi elma", "apple"]),
        new("Vişne", ["visne", "visneli", "sour cherry"]),
        new("Kiraz", ["kiraz", "kirazli", "cherry"]),
        new("Portakal", ["portakal", "portakalli", "orange"]),
        new("Mandalina", ["mandalina", "mandalinali", "tangerine"]),
        new("Nar", ["nar", "narli", "pomegranate"]),
        new("Böğürtlen", ["bogurtlen", "bogurtlenli", "blackberry"]),
        new("Ahududu & Frambuaz", ["ahududu", "ahududulu", "frambuaz", "frambuazli", "raspberry"]),
        new("Yaban Mersini", ["yaban mersini", "yaban mersinli", "blueberry"]),
        new("Tropikal", ["tropikal", "tropical", "ejder meyvesi", "passion fruit", "marakuya", "guava"]),
        new("Hindistan Cevizi", ["hindistan cevizi", "hindistancevizi", "kokonat", "coconut"]),
        new("Hindistan Cevizi & Açai", ["hindistan cevizi ve acai", "hindistancevizi ve acai", "kokonat ve acai"]),
        new("Açai", ["acai", "açai"]),
        new("Nane", ["nane", "naneli", "mint"]),
        new("Zencefil", ["zencefil", "zencefilli", "ginger"]),
        new("Bergamot & Earl Grey", ["bergamot", "bergamotlu", "earl grey"]),
        new("Yasemin", ["yasemin", "yaseminli", "jasmine"]),
        new("Papatya", ["papatya", "papatyali"]),
        new("Ihlamur", ["ihlamur"]),
        new("Adaçayı", ["adacayi"]),
        new("Rezene", ["rezene"]),
        new("Rooibos", ["rooibos"]),
        new("Kuşburnu", ["kusburnu"]),

        // Kahve / Çay Kavrum & Çeşitleri
        new("Espresso", ["espresso"]),
        new("Filtre Kahve", ["filtre kahve"]),
        new("Türk Kahvesi", ["turk kahvesi"]),
        new("Kakuleli", ["kakuleli", "kakule"]),
        new("Damla Sakızlı", ["damla sakizli", "damla sakizi"]),
        new("Koyu Kavrulmuş", ["koyu kavrulmus", "dark roast", "intenso"]),
        new("Orta Kavrulmuş", ["orta kavrulmus", "medium roast"]),
        new("Açık Kavrulmuş", ["acik kavrulmus", "light roast", "blonde"]),
        new("Kafeinsiz", ["kafeinsiz", "decaf", "decaffeinato"]),

        // ==========================================
        // 2. ATIŞTIRMALIK, ÇİKOLATA, BİSKÜVİ & KURUYEMİŞ
        // ==========================================
        new("Sütlü", ["sutlu", "milk"]),
        new("Bitter", ["bitter", "dark chocolate"]),
        new("Beyaz Çikolata", ["beyaz cikolata", "white chocolate"]),
        new("Karamel", ["karamel", "karamelli", "caramel", "tuzlu karamel"]),
        new("Fındık", ["findik", "findikli", "hazelnut"]),
        new("Antep Fıstığı", ["antep fistigi", "antep fistikli", "fistikli", "pistachio"]),
        new("Yer Fıstığı", ["yer fistigi", "yer fistikli", "peanut"]),
        new("Badem", ["badem", "bademli", "almond"]),
        new("Ceviz", ["ceviz", "cevizli", "walnut"]),
        new("Bol Tuzlu", ["bol tuzlu", "boltuzlu"]),
        new("Az Tuzlu", ["az tuzlu", "aztuzlu"]),
        new("Tuzlu", ["tuzlu", "salted"]),
        new("Tuzsuz", ["tuzsuz", "unsalted"]),
        new("Kavrulmuş", ["kavrulmus", "roasted"]),
        new("Çiğ", ["cig", "raw"]),
        new("Baharatlı", ["baharatli", "spicy"]),
        new("Tavuk Çeşnili", ["tavuk cesnili", "tavuklu"]),
        new("Peynirli", ["peynirli", "cheese"]),
        new("Peynir & Soğan", ["peynir sogan", "peynir ve sogan", "sour cream & onion"]),
        new("Meyveli", ["meyveli", "karisik meyveli"]),

        // ==========================================
        // 3. SÜT, KAHVALTILIK & TEMEL GIDA
        // ==========================================
        new("Laktozsuz", ["laktozsuz", "lactose free"]),
        new("Glutensiz", ["glutensiz", "gluten free"]),
        new("Şekersiz", ["sekersiz", "sugar free", "no sugar", "seker ilavesiz"]),
        new("Zero", ["zero"]),
        new("Light / Yağsız", ["light", "yagsiz", "skimmed"]),
        new("Yarım Yağlı", ["yarim yagli", "semi skimmed"]),
        new("Tam Yağlı", ["tam yagli"]),
        new("Yüksek Protein", ["yuksek protein", "proteinli", "high protein"]),
        new("Organik", ["organik", "organic", "bio"]),
        new("Köy / Çiftlik", ["koy", "ciftlik", "gezen tavuk"]),
        new("Sızma", ["sizma", "extra virgin", "naturel sizma"]),
        new("Riviera", ["riviera"]),

        // ==========================================
        // 4. DETERJAN, TEMİZLİK & ÇAMAŞIR BAKIMI
        // ==========================================
        new("Renkliler", ["renkliler", "renklilere ozel", "color"]),
        new("Beyazlar", ["beyazlar", "beyazlara ozel", "white"]),
        new("Siyahlar / Koyu Renkliler", ["siyahlar", "koyu renkliler", "black"]),
        new("Dağ Esintisi", ["dag esintisi"]),
        new("Bahar Çiçekleri", ["bahar cicekleri", "cicek tazeligi", "cicek bahcesi"]),
        new("Lavanta", ["lavanta", "lavantali", "lavender"]),
        new("Gül", ["gul", "gullu", "rose"]),
        new("Beyaz Sabun Ferahlığı", ["beyaz sabun", "beyaz sabun kokulu"]),
        new("Deniz Ferahlığı / Okyanus", ["okyanus", "deniz ferahligi", "ocean"]),
        new("Hassas Ciltler / Bebek", ["sensitive", "hassas ciltler", "bebek", "baby"]),
        new("Limon / Mutfak", ["limon ferahligi", "lemon fresh"]),
        new("Ekstra Hijyen / Çamaşır Suyu Etkili", ["hijyen", "camasir suyu katkili", "oxiaction"]),
        new("Sıvı", ["sivi jel", "sivi deterjan"]),
        new("Toz", ["toz deterjan"]),
        new("Kapsül", ["kapsul", "pods", "tablet"]),

        // ==========================================
        // 5. KİŞİSEL BAKIM, SABUN, ŞAMPUAN & DİŞ
        // ==========================================
        new("Cool Mint", ["cool mint", "ferah nane"]),
        new("Fresh Burst", ["fresh burst"]),
        new("Total Care", ["total care"]),
        new("Yoğun Tat", ["yogun tat"]),
        new("Hafif Tat", ["hafif tat", "ekstra hafif tat", "zero alkol"]),
        new("Beyazlatıcı / White", ["beyazlatici", "whitening", "white"]),
        new("Hassasiyet / Sensitive", ["hassasiyet", "sensitive", "sensodyne"]),
        new("Lotus Çiçeği", ["lotus", "lotus cicegi"]),
        new("Avokado & Zeytinyağı", ["avokado", "zeytinyagli", "olive oil"]),
        new("Aloe Vera", ["aloe vera", "aloe"]),
        new("Argan Yağı", ["argan", "argan yagi"]),
        new("Keratin", ["keratin"]),
        new("Kepeğe Karşı", ["kepege karsi", "anti dandruff"]),
        new("Dökülme Karşıtı", ["dokulme karsiti", "anti hair fall"]),

        // ==========================================
        // 6. ENERJİ İÇECEĞİ & ÖZEL EDİSYONLAR
        // ==========================================
        new("White Edition", ["white edition"]),
        new("Red Edition", ["red edition"]),
        new("Blue Edition", ["blue edition"]),
        new("Yellow Edition", ["yellow edition"]),
        new("Green Edition", ["green edition"]),
        new("Pink Edition", ["pink edition"]),
        new("Summer Edition", ["summer edition"]),
        new("Winter Edition", ["winter edition"]),
        new("Peach Edition", ["peach edition"]),
        new("Lilac Edition", ["lilac edition"]),
        new("Gold Edition", ["gold edition"]),
        new("Silver Edition", ["silver edition"]),

        // ==========================================
        // 7. TEKNOLOJİ & RENKLER
        // ==========================================
        new("Siyah", ["siyah", "black", "gece siyahi", "uzay grisi", "space grey"]),
        new("Beyaz", ["beyaz", "white", "kar beyazi"]),
        new("Mavi", ["mavi", "blue", "gece mavisi", "lacivert"]),
        new("Kırmızı", ["kirmizi", "red"]),
        new("Yeşil", ["yesil", "green"]),
        new("Sarı", ["sari", "yellow"]),
        new("Mor", ["mor", "purple"]),
        new("Pembe", ["pembe", "pink", "rose gold"]),
        new("Gümüş / Gri", ["gumus", "silver", "gri", "grey", "gray"])
    ];

    public static string? ExtractVariant(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var normalized = TurkishTextHelper.NormalizeToAscii(title).ToLowerInvariant();

        foreach (var def in VariantDictionary)
        {
            foreach (var kw in def.Keywords)
            {
                var kwNorm = TurkishTextHelper.NormalizeToAscii(kw).ToLowerInvariant();
                // Kelime sınırlarıyla tam eşleşme kontrolü (örn. "aci" kelimesi "hacim" içinde eşleşmesin)
                var pattern = $@"(^|[^a-z0-9]){Regex.Escape(kwNorm)}([^a-z0-9]|$)";
                if (Regex.IsMatch(normalized, pattern))
                {
                    return def.CanonicalName;
                }
            }
        }

        return null;
    }
}
