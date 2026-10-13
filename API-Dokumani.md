# Tutumlu Fiyat Karşılaştırma — API Dokümantasyonu

Bu doküman `PriceAggregator.Api` projesindeki (ASP.NET Core Minimal API, .NET 10) tüm endpoint'leri; hangi isteğin atılacağını ve karşılığında ne dönileceğini göstermek için kod üzerinden (Endpoints/*.cs) çıkarılmıştır.

## 1. Genel Bilgiler

**Base URL**
- Geliştirme (dotnet ile çalışırken): `http://localhost:5196/api` (HTTPS: `https://localhost:7249/api`)
- Frontend aynı origin'den servis edilirse (`Frontend/` klasörü dotnet tarafından da sunuluyor): `{origin}/api`

**Kimlik doğrulama (JWT Bearer)**
- `POST /api/auth/login` veya `/api/auth/register` bir `token` döner.
- Korumalı (🔒) endpoint'lere bu token, header olarak eklenir:
  ```
  Authorization: Bearer <token>
  ```
- Token içeriği: `sub` = kullanıcı Id, `email`, `displayName` claim'leri. Süresi **10080 dakika (7 gün)** (`Jwt:ExpiresMinutes`).
- Token süresi dolduğunda veya geçersizse endpoint `401 Unauthorized` döner.

**JSON alan adları (ÖNEMLİ)**
- API `PropertyNamingPolicy = null` ile yapılandırılmış, yani **camelCase'e çevrilmiyor** — response'lardaki alan adları C# tarafındaki gibi **PascalCase**'dir (`Id`, `CanonicalTitle`, `LowestPrice`, `ImageUrl` vb.), `id`/`canonicalTitle` değil.
- İstek gönderirken de body alanlarını PascalCase yazmak gerekir (ör. `Email`, `Password`).

**CORS**
- `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` — herhangi bir origin'den istek atılabilir (dev ortamı için gevşek bırakılmış).

**Genel hata formatı**
- Çoğu `400/404/409` yanıtı: `{ "error": "..." }` (mesajlar Türkçe).
- `401 Unauthorized` genelde boş body döner.

---

## 2. Auth — `/api/auth`

### POST `/api/auth/register`
Yeni kullanıcı kaydı.

**Body**
```json
{
  "Email": "ad@alanadi.com",
  "Password": "en az 6 karakter",
  "Name": "Ad",
  "Surname": "Soyad",
  "Phone": "05xxxxxxxxx",
  "BirthDate": "1995-05-20T00:00:00",
  "City": "İstanbul"
}
```
Doğrulamalar: geçerli e-posta, şifre ≥ 6 karakter, ad/soyad zorunlu, telefon `05xxxxxxxxx` veya `5xxxxxxxxx` formatında, doğum tarihi zorunlu ve bugünden ileri olamaz, şehir zorunlu.

**Yanıt — 200 OK**
```json
{
  "token": "eyJhbGciOi...",
  "user": {
    "Id": 1, "Email": "ad@alanadi.com", "DisplayName": null,
    "Name": "Ad", "Surname": "Soyad", "Phone": "05xxxxxxxxx",
    "BirthDate": "1995-05-20T00:00:00", "City": "İstanbul"
  }
}
```
**Hatalar**: `400 { error }` (doğrulama), `409 { error }` (e-posta zaten kayıtlı).

### POST `/api/auth/login`
**Body**: `{ "Email": "...", "Password": "..." }`
**Yanıt — 200 OK**: yukarıdaki ile aynı `{ token, user }` şekli.
**Hata**: `401 Unauthorized` (e-posta/şifre yanlış).

### GET `/api/auth/me` 🔒
Giriş yapmış kullanıcının bilgisini döner.
**Yanıt — 200 OK**: `user` objesi (register/login'deki ile aynı alanlar, `token` olmadan).

### PUT `/api/auth/me` 🔒
Profil güncelleme.
**Body**: `{ "Email": "...", "Name": "...", "Surname": "...", "Phone": "...", "BirthDate": "...", "City": "..." }` (Email hariç hepsi opsiyonel/nullable)
**Yanıt — 200 OK**: güncellenmiş `user` objesi.
**Hatalar**: `400 { error }`, `409 { error }` (e-posta çakışması).

### POST `/api/auth/change-password` 🔒
**Body**: `{ "OldPassword": "...", "NewPassword": "...", "ConfirmPassword": "..." }`
**Yanıt — 200 OK**: `{ "message": "Şifreniz başarıyla güncellendi." }`
**Hatalar**: `400 { error }` (eski şifre yanlış / yeni şifre < 6 karakter / eski ile aynı / eşleşmiyor).

---

## 3. Ürünler — `/api/products`

### GET `/api/products`
Filtrelenmiş, sayfalanmış ürün listesi (ana ürün grid'i / arama sonuçları).

**Query parametreleri** (hepsi opsiyonel)
| Parametre | Tip | Açıklama |
|---|---|---|
| `q` | string | Arama metni (ürün adı + marka kelime bazlı eşleşir) |
| `brand` | string | Marka adı (tam eşleşme) |
| `category` | string | Kategori `Slug`'ı (alt kategorileri de kapsar) |
| `sort` | string | `price_asc`, `price_desc`, `name_desc`, `newest` (belirtilmezse `q` varsa alaka sırası, yoksa isim A→Z) |
| `minPrice` / `maxPrice` | decimal | Fiyat aralığı |
| `attr` | string[] | Öznitelik filtresi, format: `"AttributeName:Value"`, birden çok kez tekrarlanabilir (`?attr=Renk:Kırmızı&attr=Hacim:1%20-%202%20Litre`) |
| `onSale` | bool | `true` ise sadece indirimli ürünler |
| `page` / `pageSize` | int | Sayfalama (`pageSize` varsayılan 24, maks 100) |
| `skip` | int | Sonsuz kaydırma (infinite scroll) için `page` yerine doğrudan offset — verilirse `page` yok sayılır |

**Yanıt — 200 OK**
```json
{
  "items": [
    {
      "Id": 10, "CanonicalTitle": "...", "Brand": "...",
      "LowestPrice": 49.90, "OfferCount": 3, "ImageUrl": "...",
      "UnitPrice": 12.47, "UnitLabel": "L", "OldPrice": 59.90,
      "DiscountPercent": 17, "LowestPriceStore": "Migros"
    }
  ],
  "totalCount": 120, "page": 1, "pageSize": 24, "totalPages": 5,
  "liveSearchPending": false
}
```
Not: Sonuç sayısı eşiğin (`LiveSearch:MinResultsThreshold`, varsayılan 20) altında kalan bir `q` araması, arka planda canlı kazıma (scrape) tetikler; `liveSearchPending: true` dönerse birkaç saniye sonra tekrar aynı isteği atmak daha fazla sonuç getirebilir.

### GET `/api/products/{productId}`
Tek ürün detayı (tüm satıcı teklifleriyle).

**Yanıt — 200 OK**
```json
{
  "Product": {
    "Id": 10, "CanonicalTitle": "...", "Brand": "...", "ImageUrl": "...",
    "SizeValue": 1, "SizeUnit": "L", "PackQuantity": 1, "Sku": "...",
    "CategoryPath": ["Market", "İçecek", "Su"],
    "Attributes": [ { "Name": "Renk", "Values": ["Kırmızı"] } ],
    "Specs": [ { "Group": "Genel", "Items": [ { "SpecName": "Menşei", "SpecValue": "Türkiye" } ] } ]
  },
  "Offers": [
    {
      "SellerId": 3, "CanonicalTitle": "...", "Source": "migros",
      "Price": 49.90, "ImageUrl": "...", "ProductUrl": "https://...",
      "StoreName": "Migros", "ExternalTitle": "...",
      "UnitPrice": 12.47, "UnitLabel": "L", "OldPrice": 59.90, "DiscountPercent": 17
    }
  ]
}
```
**Hata**: `404 Not Found` (ürün yok).

### GET `/api/products/{productId}/similar`
Aynı/benzer kategori veya markadan öneriler (1 saat cache'lenir).
**Yanıt — 200 OK**: `SimilarProductRow[]` → `{ Id, CanonicalTitle, Brand, LowestPrice, OfferCount, ImageUrl, SameCategory, UnitPrice, UnitLabel, LowestPriceStore }`
**Hata**: `404 Not Found` (sonuç yoksa).

### POST `/api/products/{productId}/view` 🔒
Ürün detay sayfası açıldığında loglanır (öneri motoru için sinyal). Body yok.
**Yanıt — 200 OK** (boş). **Hata**: `401 Unauthorized`.

### GET `/api/products/compare?ids=1,2,3`
Ürün karşılaştırma. `ids` virgülle ayrılmış, en fazla **4** id dikkate alınır.
**Yanıt — 200 OK**: dizi, her eleman `/api/products` item'ı + `Attributes`/`Specs` alanları eklenmiş hâli.

### GET `/api/products/recommended`
Kişiselleştirilmiş / popüler ürün önerileri. **Auth zorunlu değil** — token gönderilirse (login'liyse) geçmiş favoriler/satın alma/arama sinyallerine göre kişiselleştirilir, gönderilmezse popülerlik sırasına göre doldurulur.

**Query**: `limit` (varsayılan 10, 1–30), `onSale` (bool)
**Yanıt — 200 OK**: `{ "items": [ ...aynı ürün item şekli... ] }`

### GET `/api/products/{productId}/price-history`
Fiyat geçmişi, günlük gruplanmış.
**Yanıt — 200 OK**
```json
[
  {
    "Date": "2026-08-20",
    "MinPrice": 44.90,
    "AvgPrice": 47.10,
    "Offers": [ { "SellerName": "Migros", "Price": 44.90 } ]
  }
]
```

---

## 4. Markalar — `/api/brands`

### GET `/api/brands`
**Query**: `category` (slug), `q`, `attr[]`, `minPrice`, `maxPrice`, `onSale` — hepsi opsiyonel, `/api/products` ile aynı anlamda; verilen filtrelere uyan ürünlerin markalarını döner. Filtre yoksa 6 saat cache'lenir.
**Yanıt — 200 OK**: `string[]` (marka adları, alfabetik).

---

## 5. Kategoriler

### GET `/api/categories`
Tüm kategori ağacı (düz liste, `ParentCategoryId` ile hiyerarşi kurulur). 6 saat cache'lenir.
**Yanıt — 200 OK**: `[{ "Id": 1, "Name": "Market", "Slug": "market", "ParentCategoryId": null }, ...]`

### GET `/api/search-categories?q=...`
Bir arama terimine göre hangi kategorilerde kaç ürün eşleştiğini döner (arama sayfasındaki kategori facet'i için).
**Yanıt — 200 OK**: `[{ "CategoryId": 5, "ProductCount": 12 }, ...]` (`q` boşsa `[]`)

---

## 6. Öznitelikler (Filtreler) — `/api/attributes`

### GET `/api/attributes`
Ürün filtreleme panelindeki öznitelik grupları (Renk, Hacim, Ağırlık vb.) ve her değerin ürün sayısı.
**Query**: `category`, `q`, `brand`, `attr[]`, `minPrice`, `maxPrice`, `onSale` — `/api/products` ile aynı filtreler (facet, mevcut filtrelere göre hesaplanır). Filtre yoksa 6 saat cache'lenir.
**Yanıt — 200 OK**
```json
[
  { "Name": "Renk", "Values": [ { "AttributeValue": "Kırmızı", "ProductCount": 8 } ] }
]
```
Not: Toplam eşleşen ürün 50'den fazlaysa, en az `%1` (min 3) ürünte görülmeyen değerler listeden elenir (facet'i şişirmemek için).

---

## 7. Arama — Search

### GET `/api/search-suggestions?q=...`
Canlı arama kutusu önerileri (2+ karakterden itibaren).
**Yanıt — 200 OK**: `{ "Brands": ["..."], "Products": [ { "Id": 1, "CanonicalTitle": "...", "ImageUrl": "..." } ] }` (en fazla 5'er)

### GET `/api/popular-searches`
Son 7 günün popüler arama terimleri (Redis varsa oradan, yoksa SQL arşivinden, o da yoksa sabit varsayılan liste).
**Yanıt — 200 OK**: `[{ "Term": "süt", "Count": 10 }, ...]`

### GET `/api/search-history` 🔒
Kullanıcının son aramaları (en fazla 8, tekrarsız, en son aranan üstte).
**Yanıt — 200 OK**: `string[]`

### DELETE `/api/search-history` 🔒
Kullanıcının tüm arama geçmişini siler. **Yanıt — 200 OK** (boş).

### POST `/api/search-history/log` 🔒
Arama kutusundan doğrudan bir ürün/markaya tıklanınca (yazmadan seçim) geçmişe loglanır.
**Body**: `{ "Term": "...", "ProductId": 10, "Brand": null }` (ikisi de opsiyonel — biri verilir)
**Yanıt — 200 OK** (boş).

---

## 8. Favoriler — `/api/favorites` 🔒 (tamamı auth gerektirir)

### GET `/api/favorites`
**Yanıt — 200 OK**: `[{ Id, CanonicalTitle, Brand, LowestPrice, OfferCount, ImageUrl, UnitPrice, UnitLabel, OldPrice, DiscountPercent }, ...]`

### POST `/api/favorites/{productId}`
Ürünü favoriye ekler. Body yok.
**Yanıt — 200 OK** (boş). **Hata**: `404 { error }` (ürün bulunamadı).

### DELETE `/api/favorites/{productId}`
Favoriden çıkarır. **Yanıt — 200 OK** (boş).

---

## 9. Satın Alma Geçmişi — `/api/purchase-history` 🔒

### GET `/api/purchase-history`
**Yanıt — 200 OK**
```json
{
  "items": [
    {
      "Id": 1, "CanonicalTitle": "...", "Brand": "...", "LowestPrice": 44.90,
      "OfferCount": 3, "ImageUrl": "...", "UnitPrice": 11.2, "UnitLabel": "L",
      "OldPrice": 54.90, "DiscountPercent": 18,
      "PurchaseDate": "2026-08-20T10:00:00", "LowestPriceStore": "Migros"
    }
  ],
  "totalMoneySaved": 145.30
}
```

### POST `/api/purchase-history/confirm`
Satın alma onayı (fiyat bildirim akışından).
**Body**: `{ "ProductId": 10, "SellerId": 3, "ActualPrice": 59.90, "DiscountedPrice": 49.90 }`
**Yanıt — 200 OK**: `{ "message": "Satın alma kaydı başarıyla oluşturuldu." }`
**Hatalar**: `400 { error }` (geçersiz id'ler), `404 { error }` (ürün/satıcı bulunamadı).

---

## 10. Bildirimler — `/api/notifications` 🔒

### GET `/api/notifications`
İstek atıldığında önce favori ürünlerdeki indirimler otomatik senkronize edilir, sonra liste döner.
**Yanıt — 200 OK**: `[{ NotificationId, UserId, OnSaleProductId, DiscountedPrice, NotificationMessage, NotificationDate, ReadStatus, CanonicalTitle, ImageUrl, LowestPrice, OldPrice, DiscountPercent }, ...]`

### GET `/api/notifications/unread-count`
**Yanıt — 200 OK**: `{ "unreadCount": 3 }`

### POST `/api/notifications/{id}/read`
**Yanıt — 200 OK** (boş).

### POST `/api/notifications/read-all`
**Yanıt — 200 OK** (boş).

### DELETE `/api/notifications/{id}`
**Yanıt — 200 OK** (boş).

---

## 11. Örnek istekler (fetch)

```js
// Login
const res = await fetch(`${API_BASE}/auth/login`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ Email: "test@test.com", Password: "123456" }),
});
const { token, user } = await res.json();

// Auth gerektiren bir istek (favorilere ekleme)
await fetch(`${API_BASE}/favorites/10`, {
  method: "POST",
  headers: { Authorization: `Bearer ${token}` },
});

// Ürün listesi — filtreli
const list = await fetch(
  `${API_BASE}/products?q=süt&category=market&sort=price_asc&page=1&pageSize=24`
).then(r => r.json());
```

`API_BASE`, projedeki `Frontend/js/shared/api.js` içinde `http://localhost:5196/api` (veya aynı origin `/api`) olarak tanımlı.

---

*Bu doküman `PriceAggregator.Api/Endpoints/*.cs`, `Extensions/*.cs`, `Program.cs`, `appsettings.json` ve `Frontend/js/shared/api.js` / `auth.js` dosyaları incelenerek 28.08.2026 tarihinde oluşturulmuştur. Kod değiştikçe güncel tutulmalıdır.*
