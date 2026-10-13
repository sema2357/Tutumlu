export const $ = (id) => document.getElementById(id);

export function formatPrice(price) {
  return new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: "TRY",
    minimumFractionDigits: 2,
  }).format(price);
}

// UnitPrice/UnitLabel are only present when the title had a parseable size (see
// SizeParser/UnitPriceCalculator on the API side) — absent for e.g. furniture.
export function formatUnitPrice(unitPrice, unitLabel) {
  if (unitPrice == null || !unitLabel) return "";
  return `<span class="product-unit-price">${formatPrice(unitPrice)} / ${escapeHtml(unitLabel)}</span>`;
}

export function escapeHtml(text) {
  const div = document.createElement("div");
  div.textContent = text ?? "";
  return div.innerHTML;
}

export function isValidEmail(email) {
  if (!email || email.length > 255) return false;
  return /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(email);
}

export function isValidPhone(phone) {
  if (!phone) return false;
  const digits = phone.replace(/\D/g, "");
  return /^(0?5\d{9})$/.test(digits);
}

export function formatTimeAgo(dateString) {
  if (!dateString) return "";
  const d = new Date(dateString);
  if (isNaN(d.getTime())) return dateString;

  const now = new Date();
  const diffSec = Math.floor((now - d) / 1000);

  if (diffSec < 60) return "Az önce";
  if (diffSec < 3600) return `${Math.floor(diffSec / 60)} dakika önce`;
  if (diffSec < 86400) return `${Math.floor(diffSec / 3600)} saat önce`;
  if (diffSec < 604800) return `${Math.floor(diffSec / 86400)} gün önce`;

  return d.toLocaleDateString("tr-TR", { day: "numeric", month: "short" });
}

// Türkiye'nin 81 ili — tek kaynak, tüm şehir seçiciler buradan doldurulur.
export const TURKISH_CITIES = [
  "Adana", "Adıyaman", "Afyonkarahisar", "Ağrı", "Aksaray", "Amasya",
  "Ankara", "Antalya", "Ardahan", "Artvin", "Aydın", "Balıkesir",
  "Bartın", "Batman", "Bayburt", "Bilecik", "Bingöl", "Bitlis",
  "Bolu", "Burdur", "Bursa", "Çanakkale", "Çankırı", "Çorum",
  "Denizli", "Diyarbakır", "Düzce", "Edirne", "Elazığ", "Erzincan",
  "Erzurum", "Eskişehir", "Gaziantep", "Giresun", "Gümüşhane", "Hakkari",
  "Hatay", "Iğdır", "Isparta", "İstanbul", "İzmir", "Kahramanmaraş",
  "Karabük", "Karaman", "Kars", "Kastamonu", "Kayseri", "Kilis",
  "Kırıkkale", "Kırklareli", "Kırşehir", "Kocaeli", "Konya", "Kütahya",
  "Malatya", "Manisa", "Mardin", "Mersin", "Muğla", "Muş",
  "Nevşehir", "Niğde", "Ordu", "Osmaniye", "Rize", "Sakarya",
  "Samsun", "Şanlıurfa", "Siirt", "Sinop", "Şırnak", "Sivas",
  "Tekirdağ", "Tokat", "Trabzon", "Tunceli", "Uşak", "Van",
  "Yalova", "Yozgat", "Zonguldak",
];

// Verilen id'deki <select> öğesini TURKISH_CITIES ile doldurur.
// Mevcut seçili değeri (varsa) korur.
export function populateCitySelect(id) {
  const select = $(id);
  if (!select) return;
  const current = select.value;
  select.innerHTML =
    `<option value="">Seçiniz</option>` +
    TURKISH_CITIES.map((c) => `<option>${c}</option>`).join("");
  if (current) select.value = current;
}
