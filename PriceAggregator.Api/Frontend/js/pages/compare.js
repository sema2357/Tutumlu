// Entry point for compare.html — header/search/category-menu wiring mirrors home.js (no
// in-page filtering, search just navigates to products.html like the homepage does).
import { $, formatPrice, formatUnitPrice, escapeHtml } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { initCategoryMenu, loadCategories, closeCategoryMenu } from "../modules/categoryMenu.js";
import { initModal, closeModal } from "../modules/modal.js";
import { initLoginModal, closeLoginModal, closeLogoutModal } from "../modules/loginModal.js";
import { initPurchaseNotifier } from "../shared/purchaseNotifier.js";
import { initSearchBox } from "../modules/search.js";
import { getCompareItems, clearCompare } from "../shared/compare.js";

function goToProducts(params) {
  const qs = new URLSearchParams(params).toString();
  window.location.href = qs ? `products.html?${qs}` : "products.html";
}

initCategoryMenu((slug) => goToProducts({ category: slug }));
initModal();
initLoginModal();
initPurchaseNotifier();
initSearchBox({
  onSubmit: (term) => {
    if (term) goToProducts({ q: term });
  },
  onBrandPick: (brand) => goToProducts({ brand }),
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    closeCategoryMenu();
    closeModal();
    closeLoginModal();
    closeLogoutModal();
  }
});

loadCategories();

// The list to compare is read from the URL if present (so a compare link is shareable/
// refreshable on its own) — otherwise falls back to whatever's currently in the client-side
// compare selection. From here on `currentIds` is this page's own local state: removing a
// product from the table (below) only touches `currentIds`, not the shared selection.
//
// The shared selection is cleared right away so that going back to the product grid doesn't
// still show these products as selected for comparison — a compare list is meant to be
// consumed once you've viewed it, not stay "checked" indefinitely.
const urlIds = new URLSearchParams(window.location.search).get("ids");
let currentIds = urlIds
  ? urlIds
      .split(",")
      .map((s) => Number(s.trim()))
      .filter((n) => Number.isInteger(n) && n > 0)
  : getCompareItems().map((p) => p.id);

clearCompare();

function showEmptyState() {
  $("compareContent").innerHTML = `
    <div class="compare-empty-state">
      <p>Karşılaştırmak için ürün seçin.</p>
      <a href="products.html">Ürünlere göz at</a>
    </div>`;
}

function attributeUnion(products) {
  const names = [];
  products.forEach((p) => {
    (p.Attributes || []).forEach((a) => {
      if (!names.includes(a.Name)) names.push(a.Name);
    });
  });
  return names;
}

function attributeValue(product, name) {
  const attr = (product.Attributes || []).find((a) => a.Name === name);
  return attr && attr.Values.length > 0 ? attr.Values.join(", ") : null;
}

function emptyCell() {
  return `<span class="compare-empty-value">—</span>`;
}

// Specs (nutrition values, ingredients, material, dimensions, ...) come from the source
// store's own product-detail page — see IProductSpecScraper on the backend — and are grouped
// (SpecGroup) the same way the single-product info modal shows them, so a comparison of two
// grocery items can show "Beslenme Değerleri" side by side instead of just Renk/Malzeme-style
// facets.
function specGroupUnion(products) {
  const groups = [];
  products.forEach((p) => {
    (p.Specs || []).forEach((g) => {
      if (!groups.includes(g.Group)) groups.push(g.Group);
    });
  });
  return groups;
}

function specNameUnion(products, group) {
  const names = [];
  products.forEach((p) => {
    const g = (p.Specs || []).find((s) => s.Group === group);
    (g ? g.Items : []).forEach((i) => {
      if (!names.includes(i.SpecName)) names.push(i.SpecName);
    });
  });
  return names;
}

function specValue(product, group, name) {
  const g = (product.Specs || []).find((s) => s.Group === group);
  const item = g ? g.Items.find((i) => i.SpecName === name) : null;
  return item ? item.SpecValue : null;
}

function renderTable(products) {
  const attrNames = attributeUnion(products);

  const headCells = products
    .map(
      (p) => `
      <th class="compare-product-col">
        <div class="compare-product-head">
          <button type="button" class="compare-product-remove" data-remove-id="${p.Id}" aria-label="Karşılaştırmadan çıkar">×</button>
          ${p.ImageUrl ? `<img class="compare-product-image" src="${escapeHtml(p.ImageUrl)}" alt="">` : ""}
          ${p.Brand ? `<span class="compare-product-brand">${escapeHtml(p.Brand)}</span>` : ""}
          <span class="compare-product-title">${escapeHtml(p.CanonicalTitle)}</span>
        </div>
      </th>`
    )
    .join("");

  const validPrices = products
    .map((p) => p.LowestPrice)
    .filter((price) => price != null && price > 0);
  const minComparePrice = validPrices.length > 0 ? Math.min(...validPrices) : null;

  const priceRow = products
    .map((p) => {
      const isCheapest = minComparePrice != null && p.LowestPrice === minComparePrice;
      return `<td>
        <div class="compare-price-cell">
          <span class="compare-price">${formatPrice(p.LowestPrice)}</span>
          ${isCheapest ? '<span class="compare-cheapest-badge">En ucuz</span>' : ""}
        </div>
      </td>`;
    })
    .join("");
  const unitPriceRow = products.map((p) => `<td>${formatUnitPrice(p.UnitPrice, p.UnitLabel) || emptyCell()}</td>`).join("");
  const storeRow = products.map((p) => `<td>${p.OfferCount} mağaza</td>`).join("");
  const discountRow = products.map((p) => `<td>${p.DiscountPercent ? `%${p.DiscountPercent}` : emptyCell()}</td>`).join("");

  const attrRows = attrNames
    .map((name) => {
      const cells = products
        .map((p) => {
          const value = attributeValue(p, name);
          return `<td>${value ? escapeHtml(value) : emptyCell()}</td>`;
        })
        .join("");
      return `<tr><td>${escapeHtml(name)}</td>${cells}</tr>`;
    })
    .join("");

  const specGroups = specGroupUnion(products);
  const specRows = specGroups
    .map((group) => {
      const names = specNameUnion(products, group);
      const rows = names
        .map((name) => {
          const cells = products
            .map((p) => {
              const value = specValue(p, group, name);
              return `<td>${value ? escapeHtml(value) : emptyCell()}</td>`;
            })
            .join("");
          return `<tr><td>${escapeHtml(name)}</td>${cells}</tr>`;
        })
        .join("");
      return `<tr class="compare-group-row"><td colspan="${products.length + 1}">${escapeHtml(group)}</td></tr>${rows}`;
    })
    .join("");

  $("compareContent").innerHTML = `
    <div class="compare-table-scroll">
      <table class="compare-table">
        <thead><tr><th></th>${headCells}</tr></thead>
        <tbody>
          <tr><td>Fiyat</td>${priceRow}</tr>
          <tr><td>Birim fiyat</td>${unitPriceRow}</tr>
          <tr><td>Mağaza sayısı</td>${storeRow}</tr>
          <tr><td>İndirim</td>${discountRow}</tr>
          ${attrRows}
          ${specRows}
        </tbody>
      </table>
    </div>`;

  $("compareContent")
    .querySelectorAll("[data-remove-id]")
    .forEach((btn) => {
      btn.addEventListener("click", () => {
        currentIds = currentIds.filter((id) => id !== Number(btn.dataset.removeId));
        loadCompare();
      });
    });
}

async function loadCompare() {
  if (currentIds.length === 0) {
    showEmptyState();
    return;
  }

  $("compareContent").innerHTML = `<p class="compare-loading">Yükleniyor…</p>`;

  try {
    const res = await fetch(`${API_BASE}/products/compare?ids=${currentIds.join(",")}`);
    if (!res.ok) throw new Error(`API returned ${res.status}`);
    const products = await res.json();

    if (!products || products.length === 0) {
      showEmptyState();
      return;
    }

    renderTable(products);
  } catch (err) {
    console.error("Karşılaştırma verisi yüklenemedi:", err);
    $("compareContent").innerHTML = `<p class="compare-loading">Ürünler yüklenemedi.</p>`;
  }
}

loadCompare();
