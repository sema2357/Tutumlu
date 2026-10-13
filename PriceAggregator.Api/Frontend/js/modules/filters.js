import { $, escapeHtml } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { state, categories } from "../shared/state.js";
import { syncCategorySelectionHighlight, selectCategory, getCategoryChain } from "./categoryMenu.js";
import { loadProducts } from "./productGrid.js";

// Level 1 categories (e.g. Market's "Atıştırmalık", "İçecek") that the current search word's
// results actually sit in — powers the filter panel's category facet, see loadSearchCategories.
let searchCategoryFacet = [];

// value can be a plain string or an array (repeated query params, e.g. attr=A&attr=B).
function buildQueryString(params) {
  const usp = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "" || value === false) continue;
    if (Array.isArray(value)) {
      value.forEach((v) => usp.append(key, v));
    } else {
      usp.append(key, value === true ? "true" : value);
    }
  }
  return usp.toString();
}

// Re-fetches both facet panels against the *current* state (q/brand/attrs/price/onSale), not
// just the current category. Call this any time a filter that isn't category itself changes —
// otherwise the brand dropdown / attribute checkboxes keep showing options computed from a
// filter combo that's no longer what's actually on screen.
export function refreshFilterPanels() {
  renderSubCategoryFilter();
  loadStores(state.category);
  loadBrands(state.category);
  loadAttributes(state.category);
}

export function updateStoreBadge() {
  const badge = $("storeSelectedBadge");
  if (!badge) return;
  const count = (state.stores || []).length;
  if (count > 0) {
    badge.textContent = count;
    badge.hidden = false;
  } else {
    badge.hidden = true;
  }
}

export function updateBrandBadge() {
  const badge = $("brandSelectedBadge");
  if (!badge) return;
  const count = (state.brands || []).length;
  if (count > 0) {
    badge.textContent = count;
    badge.hidden = false;
  } else {
    badge.hidden = true;
  }
}

export function renderActiveFilters() {
  updateStoreBadge();
  updateBrandBadge();
  const container = $("activeFilters");
  container.innerHTML = "";

  const tags = [];
  if (state.q) tags.push({ key: "q", label: `"${state.q}"` });
  if (state.category) {
    const cat = categories.find((c) => c.Slug === state.category);
    tags.push({ key: "category", label: cat ? cat.Name : state.category });
  }
  if (state.minPrice) tags.push({ key: "minPrice", label: `Min ${state.minPrice} TL` });
  if (state.maxPrice) tags.push({ key: "maxPrice", label: `Max ${state.maxPrice} TL` });

  // Store tags
  (state.stores || []).forEach((storeKey) => {
    tags.push({ key: "store", value: storeKey, label: `Mağaza: ${storeKey}` });
  });

  // Multi-brand tags
  (state.brands || []).forEach((brandName) => {
    tags.push({ key: "brand", value: brandName, label: `Marka: ${brandName}` });
  });
  if (state.brand && !(state.brands || []).includes(state.brand)) {
    tags.push({ key: "brand_legacy", label: `Marka: ${state.brand}` });
  }

  tags.forEach(({ key, value, label }) => {
    const tag = document.createElement("span");
    tag.className = "filter-tag";
    tag.innerHTML = `${escapeHtml(label)} <button type="button" aria-label="Kaldır">×</button>`;
    tag.querySelector("button").addEventListener("click", () => {
      if (key === "category") {
        state.category = "";
        syncCategorySelectionHighlight();
        renderSubCategoryFilter();
      } else if (key === "store") {
        state.stores = (state.stores || []).filter((s) => s !== value);
      } else if (key === "brand") {
        state.brands = (state.brands || []).filter((b) => b !== value);
      } else if (key === "brand_legacy") {
        state.brand = "";
      } else {
        state[key] = "";
      }
      state.page = 1;
      if (key === "q") {
        loadSearchCategories("");
      }
      syncFilterInputs();
      refreshFilterPanels();
      loadProducts(1);
    });
    container.appendChild(tag);
  });

  if (state.onSale) {
    const tag = document.createElement("span");
    tag.className = "filter-tag";
    tag.innerHTML = `Kampanyalı <button type="button" aria-label="Kaldır">×</button>`;
    tag.querySelector("button").addEventListener("click", () => {
      state.onSale = false;
      state.page = 1;
      syncFilterInputs();
      refreshFilterPanels();
      loadProducts(1);
    });
    container.appendChild(tag);
  }

  if (state.sort === "newest") {
    const tag = document.createElement("span");
    tag.className = "filter-tag";
    tag.innerHTML = `Yeni Ürünler <button type="button" aria-label="Kaldır">×</button>`;
    tag.querySelector("button").addEventListener("click", () => {
      state.sort = "name";
      state.page = 1;
      syncFilterInputs();
      loadProducts(1);
    });
    container.appendChild(tag);
  }

  state.attrs.forEach((attrKey) => {
    const tag = document.createElement("span");
    tag.className = "filter-tag";
    tag.innerHTML = `${escapeHtml(attrKey.replace(":", ": "))} <button type="button" aria-label="Kaldır">×</button>`;
    tag.querySelector("button").addEventListener("click", () => {
      state.attrs = state.attrs.filter((a) => a !== attrKey);
      state.page = 1;
      const checkbox = document.querySelector(`#attributeFilters input[value="${CSS.escape(attrKey)}"]`);
      if (checkbox) checkbox.checked = false;
      renderActiveFilters();
      refreshFilterPanels();
      loadProducts(1);
    });
    container.appendChild(tag);
  });
}

export function syncFilterInputs() {
  $("searchInput").value = state.q;
  $("sortFilter").value = state.sort;
  $("minPrice").value = state.minPrice;
  $("maxPrice").value = state.maxPrice;
  $("onSaleFilter").checked = state.onSale;
  $("newFilter").checked = state.sort === "newest";
}

export function readFilterInputs() {
  state.q = $("searchInput").value.trim();
  state.sort = $("sortFilter").value;
  state.minPrice = $("minPrice").value;
  state.maxPrice = $("maxPrice").value;
  state.onSale = $("onSaleFilter").checked;
}

// Loads and renders multi-select Stores (Migros, Teknosa, vb.) with product counts
export async function loadStores(category) {
  const container = $("storeCheckboxList");
  if (!container) return;

  try {
    const qs = buildQueryString({
      category,
      q: state.q,
      brand: state.brands && state.brands.length > 0 ? state.brands : (state.brand ? [state.brand] : []),
      attr: state.attrs,
      minPrice: state.minPrice,
      maxPrice: state.maxPrice,
      onSale: state.onSale,
    });
    const url = `${API_BASE}/stores${qs ? `?${qs}` : ""}`;
    const res = await fetch(url);
    if (!res.ok) return;
    const stores = await res.json();

    container.innerHTML = "";
    if (stores.length === 0) {
      const emptyLabel = document.createElement("span");
      emptyLabel.className = "attribute-count";
      emptyLabel.textContent = "Uygun mağaza yok";
      container.appendChild(emptyLabel);
      return;
    }

    stores.forEach((store) => {
      const item = document.createElement("label");
      item.className = "attribute-checkbox";
      const isChecked = (state.stores || []).includes(store.Name) || (state.stores || []).includes(store.DisplayName);
      item.innerHTML = `<input type="checkbox" value="${escapeHtml(store.Name)}" ${isChecked ? "checked" : ""}> <span>${escapeHtml(store.DisplayName)}</span> <span class="attribute-count">${store.ProductCount}</span>`;

      const checkbox = item.querySelector("input");
      checkbox.addEventListener("change", () => {
        if (!state.stores) state.stores = [];
        if (checkbox.checked) {
          if (!state.stores.includes(store.Name)) state.stores.push(store.Name);
        } else {
          state.stores = state.stores.filter((s) => s !== store.Name);
        }
        state.page = 1;
        renderActiveFilters();
        refreshFilterPanels();
        loadProducts(1);
      });
      container.appendChild(item);
    });
  } catch (err) {
    console.error("Failed to load stores:", err);
  }
}

// Loads and renders multi-select Brands with search box, product counts and show-more
let allLoadedBrands = [];
export async function loadBrands(category) {
  const container = $("brandCheckboxList");
  const searchBox = $("brandSearchBox");
  if (!container) return;

  try {
    const qs = buildQueryString({
      category,
      q: state.q,
      store: state.stores || [],
      attr: state.attrs,
      minPrice: state.minPrice,
      maxPrice: state.maxPrice,
      onSale: state.onSale,
    });
    const url = `${API_BASE}/brands${qs ? `?${qs}` : ""}`;
    const res = await fetch(url);
    if (!res.ok) return;
    allLoadedBrands = await res.json();

    renderBrandList(container, allLoadedBrands, $("brandSearchInput")?.value || "");

    if (searchBox) {
      searchBox.hidden = allLoadedBrands.length < 6;
      const searchInput = $("brandSearchInput");
      if (searchInput && !searchInput.dataset.bound) {
        searchInput.dataset.bound = "true";
        searchInput.addEventListener("input", () => {
          renderBrandList(container, allLoadedBrands, searchInput.value.trim());
        });
      }
    }
  } catch (err) {
    console.error("Failed to load brands:", err);
  }
}

function renderBrandList(container, brands, query = "") {
  container.innerHTML = "";
  let filtered = brands;
  if (query) {
    const qLower = query.toLocaleLowerCase("tr");
    filtered = brands.filter((b) => b.Name.toLocaleLowerCase("tr").includes(qLower));
  }

  if (filtered.length === 0) {
    const emptyMsg = document.createElement("span");
    emptyMsg.className = "attribute-count";
    emptyMsg.textContent = "Marka bulunamadı";
    container.appendChild(emptyMsg);
    return;
  }

  const COLLAPSE_LIMIT = 6;
  const showAll = query.length > 0 || container.dataset.expanded === "true";

  filtered.forEach((brand, idx) => {
    const item = document.createElement("label");
    item.className = "attribute-checkbox";
    if (!showAll && idx >= COLLAPSE_LIMIT) {
      item.hidden = true;
    }
    const isChecked = (state.brands || []).includes(brand.Name);
    item.innerHTML = `<input type="checkbox" value="${escapeHtml(brand.Name)}" ${isChecked ? "checked" : ""}> <span>${escapeHtml(brand.Name)}</span> <span class="attribute-count">${brand.ProductCount}</span>`;

    const checkbox = item.querySelector("input");
    checkbox.addEventListener("change", () => {
      if (!state.brands) state.brands = [];
      if (checkbox.checked) {
        if (!state.brands.includes(brand.Name)) state.brands.push(brand.Name);
      } else {
        state.brands = state.brands.filter((b) => b !== brand.Name);
      }
      state.page = 1;
      renderActiveFilters();
      refreshFilterPanels();
      loadProducts(1);
    });
    container.appendChild(item);
  });

  if (!query && filtered.length > COLLAPSE_LIMIT) {
    const moreBtn = document.createElement("button");
    moreBtn.type = "button";
    moreBtn.className = "attribute-show-more";
    moreBtn.textContent = showAll ? "Daha az göster" : `Daha fazla göster (+${filtered.length - COLLAPSE_LIMIT})`;
    moreBtn.addEventListener("click", () => {
      container.dataset.expanded = showAll ? "false" : "true";
      renderBrandList(container, brands, query);
    });
    container.appendChild(moreBtn);
  }
}

// Renders one checkbox group per attribute name (e.g. "Renk" under Mobilya, "Özellik" under
// Market) — only the ones that actually have data for the current category, per
// /api/attributes.
export async function loadAttributes(category) {
  const container = $("attributeFilters");
  if (!container) return;

  // Bütün ürünlerin listelendiği yerde (kategori seçilmediğinde) varyant filtrelerini gösterme
  if (!category) {
    container.innerHTML = "";
    if (state.attrs && state.attrs.length > 0) {
      state.attrs = [];
      renderActiveFilters();
    }
    return;
  }

  try {
    const qs = buildQueryString({
      category: category || "",
      q: state.q,
      brand: state.brands && state.brands.length > 0 ? state.brands : (state.brand ? [state.brand] : []),
      store: state.stores || [],
      attr: state.attrs,
      minPrice: state.minPrice,
      maxPrice: state.maxPrice,
      onSale: state.onSale,
    });
    const url = `${API_BASE}/attributes?${qs}`;
    const res = await fetch(url);
    if (!res.ok) return;
    const groups = (await res.json()).filter(g => g.Name !== "Renk");

    container.innerHTML = "";
    const validKeys = new Set();

    groups.forEach((group) => {
      const wrap = document.createElement("div");
      wrap.className = "filter-group collapsible-filter";

      const selectedCount = group.Values.filter((v) =>
        state.attrs.includes(`${group.Name}:${v.AttributeValue}`)
      ).length;

      const header = document.createElement("div");
      header.className = "filter-group-header";
      header.setAttribute("role", "button");
      header.setAttribute("tabindex", "0");
      header.setAttribute("aria-expanded", "true");
      header.innerHTML = `
        <span class="filter-group-title">
          ${escapeHtml(group.Name)}
          <span class="filter-selected-badge" ${selectedCount > 0 ? "" : "hidden"}>${selectedCount}</span>
        </span>
        <span class="filter-toggle-icon" aria-hidden="true">
          <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </span>
      `;

      const toggleGroup = () => {
        const isCollapsed = wrap.classList.toggle("collapsed");
        header.setAttribute("aria-expanded", isCollapsed ? "false" : "true");
      };
      header.addEventListener("click", toggleGroup);
      header.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          toggleGroup();
        }
      });
      wrap.appendChild(header);

      const body = document.createElement("div");
      body.className = "filter-group-body";

      const list = document.createElement("div");
      list.className = "attribute-checkbox-list";

      const COLLAPSE_AFTER = 5;

      group.Values.forEach((v, index) => {
        const key = `${group.Name}:${v.AttributeValue}`;
        validKeys.add(key);

        const item = document.createElement("label");
        item.className = "attribute-checkbox";
        if (index >= COLLAPSE_AFTER) item.hidden = true;
        item.innerHTML = `<input type="checkbox" value="${escapeHtml(key)}"> <span>${escapeHtml(v.AttributeValue)}</span> <span class="attribute-count">${v.ProductCount}</span>`;

        const checkbox = item.querySelector("input");
        checkbox.checked = state.attrs.includes(key);
        checkbox.addEventListener("change", () => {
          if (checkbox.checked) {
            if (!state.attrs.includes(key)) state.attrs.push(key);
          } else {
            state.attrs = state.attrs.filter((a) => a !== key);
          }
          renderActiveFilters();
          refreshFilterPanels();
          loadProducts(1);
        });
        list.appendChild(item);
      });

      if (group.Values.length > COLLAPSE_AFTER) {
        const moreBtn = document.createElement("button");
        moreBtn.type = "button";
        moreBtn.className = "attribute-show-more";
        moreBtn.textContent = `Daha fazla göster (+${group.Values.length - COLLAPSE_AFTER})`;
        moreBtn.addEventListener("click", () => {
          const hiddenItems = list.querySelectorAll(".attribute-checkbox[hidden]");
          if (hiddenItems.length > 0) {
            hiddenItems.forEach((it) => (it.hidden = false));
            moreBtn.textContent = "Daha az göster";
          } else {
            list.querySelectorAll(".attribute-checkbox").forEach((it, idx) => {
              if (idx >= COLLAPSE_AFTER) it.hidden = true;
            });
            moreBtn.textContent = `Daha fazla göster (+${group.Values.length - COLLAPSE_AFTER})`;
          }
        });
        list.appendChild(moreBtn);
      }

      body.appendChild(list);
      wrap.appendChild(body);
      container.appendChild(wrap);
    });

    state.attrs = state.attrs.filter((k) => validKeys.has(k));
    renderActiveFilters();
  } catch (err) {
    console.error("Failed to load attributes:", err);
  }
}

// Kategori seçildiğinde alt kategorilerini filtre olarak listeler.
// Arama yapıldığında ise kategori filtresi tamamen gizlenir.
export function renderSubCategoryFilter() {
  const group = $("subCategoryFilterGroup") || $("searchCategoryFilterGroup");
  const select = $("subCategoryFilter") || $("searchCategoryFilter");
  if (!group || !select) return;

  // Görev 2: Bir ürün arandığında (state.q doluysa) filtrelerde kategori seçeneği olmasın
  if (state.q) {
    group.hidden = true;
    select.innerHTML = '<option value="">Tümü</option>';
    return;
  }

  // Görev 1: Kategoriye tıklandığında alt kategorileri filtre olarak görünsün
  if (!state.category) {
    group.hidden = true;
    select.innerHTML = '<option value="">Tümü</option>';
    return;
  }

  const currentCat = categories.find((c) => c.Slug === state.category);
  if (!currentCat) {
    group.hidden = true;
    return;
  }

  // Seçilen kategorinin doğrudan alt dalları
  const children = categories
    .filter((c) => c.ParentCategoryId === currentCat.Id && (c.ProductCount || 0) > 0)
    .sort((a, b) => a.Name.localeCompare(b.Name, "tr"));

  if (children.length > 0) {
    group.hidden = false;
    let html = `<option value="${escapeHtml(currentCat.Slug)}">Tümü (${escapeHtml(currentCat.Name)})</option>`;
    children.forEach((sub) => {
      html += `<option value="${escapeHtml(sub.Slug)}">${escapeHtml(sub.Name)} (${sub.ProductCount})</option>`;
    });
    select.innerHTML = html;
    select.value = currentCat.Slug;
  } else if (currentCat.ParentCategoryId != null) {
    // Seçilen kategori en alt dal ise aynı üst kategorideki kardeş kategorileri listele
    const parentCat = categories.find((c) => c.Id === currentCat.ParentCategoryId);
    const siblings = parentCat
      ? categories
          .filter((c) => c.ParentCategoryId === parentCat.Id && (c.ProductCount || 0) > 0)
          .sort((a, b) => a.Name.localeCompare(b.Name, "tr"))
      : [];

    if (siblings.length > 0) {
      group.hidden = false;
      let html = parentCat
        ? `<option value="${escapeHtml(parentCat.Slug)}">Tümü (${escapeHtml(parentCat.Name)})</option>`
        : '<option value="">Tümü</option>';
      siblings.forEach((sib) => {
        html += `<option value="${escapeHtml(sib.Slug)}">${escapeHtml(sib.Name)} (${sib.ProductCount})</option>`;
      });
      select.innerHTML = html;
      select.value = currentCat.Slug;
    } else {
      group.hidden = true;
    }
  } else {
    group.hidden = true;
  }
}

export const renderSearchCategoryFilter = renderSubCategoryFilter;
export async function loadSearchCategories() {
  renderSubCategoryFilter();
}

export function clearFilters() {
  // Category is deliberately left alone — "Temizle" lives in the Filtreler panel, not the
  // category menu, so it should reset brand/price/sort/attrs without kicking the shopper
  // back out to "all products".
  state.q = "";
  state.brand = "";
  state.brands = [];
  state.stores = [];
  state.sort = "name";
  state.minPrice = "";
  state.maxPrice = "";
  state.attrs = [];
  state.onSale = false;
  state.page = 1;
  const brandSearch = $("brandSearchInput");
  if (brandSearch) brandSearch.value = "";
  loadSearchCategories("");
  syncFilterInputs();
  syncCategorySelectionHighlight();
  refreshFilterPanels();
  loadProducts(1);
}

export function initFilters() {
  const triggerApply = () => {
    state.page = 1;
    readFilterInputs();
    refreshFilterPanels();
    loadProducts(1);
  };

  $("applyFilters")?.addEventListener("click", triggerApply);

  const handlePriceEnter = (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      triggerApply();
    }
  };
  $("minPrice")?.addEventListener("keydown", handlePriceEnter);
  $("maxPrice")?.addEventListener("keydown", handlePriceEnter);

  $("sortFilter")?.addEventListener("change", () => {
    state.page = 1;
    readFilterInputs();
    syncFilterInputs(); // keep the "Sadece yeni ürünler" checkbox in sync with the sort select
    loadProducts(1);
  });

  const subCategorySelect = $("subCategoryFilter") || $("searchCategoryFilter");
  if (subCategorySelect) {
    subCategorySelect.addEventListener("change", () => {
      const selectedSlug = subCategorySelect.value;
      if (!selectedSlug) return;
      state.page = 1;
      state.category = selectedSlug;
      state.brand = "";
      state.brands = [];
      state.attrs = [];
      syncCategorySelectionHighlight();
      refreshFilterPanels();
      loadProducts(1);
    });
  }

  $("onSaleFilter").addEventListener("change", () => {
    state.page = 1;
    state.onSale = $("onSaleFilter").checked;
    refreshFilterPanels();
    loadProducts(1);
  });

  $("newFilter").addEventListener("change", () => {
    state.page = 1;
    state.sort = $("newFilter").checked ? "newest" : "name";
    syncFilterInputs();
    loadProducts(1);
  });

  $("clearFilters").addEventListener("click", clearFilters);

  const storeHeader = $("storeFilterHeader");
  const storeGroup = $("storeFilterGroup");
  if (storeHeader && storeGroup) {
    const toggleStore = () => {
      const isCollapsed = storeGroup.classList.toggle("collapsed");
      storeHeader.setAttribute("aria-expanded", isCollapsed ? "false" : "true");
    };

    storeHeader.addEventListener("click", toggleStore);
    storeHeader.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        toggleStore();
      }
    });
  }

  const brandHeader = $("brandFilterHeader");
  const brandGroup = $("brandFilterGroup");
  if (brandHeader && brandGroup) {
    const toggleBrand = () => {
      const isCollapsed = brandGroup.classList.toggle("collapsed");
      brandHeader.setAttribute("aria-expanded", isCollapsed ? "false" : "true");
    };

    brandHeader.addEventListener("click", toggleBrand);
    brandHeader.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        toggleBrand();
      }
    });
  }
}
