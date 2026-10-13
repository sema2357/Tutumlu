import { $, formatPrice, formatUnitPrice, escapeHtml } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { state } from "../shared/state.js";
import { getSession, getToken } from "../shared/auth.js";
import { isFavorited, toggleFavorite, onFavoritesChange } from "../shared/favorites.js";
import { isCompared, toggleCompare, onCompareChange } from "../shared/compare.js";
import { renderActiveFilters } from "./filters.js";
import { openProductDetail } from "./modal.js";
import { openLoginModal } from "./loginModal.js";
import { getCategoryIcon, selectCategory } from "./categoryMenu.js";

const HEART_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
  <path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z" />
</svg>`;

const COMPARE_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
  <path d="M8 3v14m0 0-4-4m4 4 4-4" />
  <path d="M16 21V7m0 0-4 4m4-4 4 4" />
</svg>`;

function buildFavoriteButton(productId) {
  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "product-favorite-btn" + (isFavorited(productId) ? " active" : "");
  btn.dataset.favoriteProductId = productId;
  btn.setAttribute("aria-label", "Favorilere ekle");
  btn.innerHTML = HEART_ICON;

  btn.addEventListener("keydown", (e) => e.stopPropagation());
  btn.addEventListener("click", async (e) => {
    e.stopPropagation();
    if (!getSession()) {
      openLoginModal();
      return;
    }
    btn.disabled = true;
    try {
      await toggleFavorite(productId);
    } catch (err) {
      console.error("Favori güncellenemedi:", err);
    } finally {
      btn.disabled = false;
    }
  });

  return btn;
}

function buildCompareButton(product) {
  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "product-compare-btn" + (isCompared(product.Id) ? " active" : "");
  btn.dataset.compareProductId = product.Id;
  btn.setAttribute("aria-label", "Karşılaştırmaya ekle");
  btn.innerHTML = COMPARE_ICON;

  btn.addEventListener("keydown", (e) => e.stopPropagation());
  btn.addEventListener("click", (e) => {
    e.stopPropagation();
    const added = toggleCompare({ id: product.Id, title: product.CanonicalTitle, imageUrl: product.ImageUrl });
    if (!added) {
      btn.classList.remove("shake");
      // Force a reflow so the animation restarts if the user hits the limit twice in a row.
      void btn.offsetWidth;
      btn.classList.add("shake");
    }
  });

  return btn;
}

// Keeps every rendered heart/compare button (a product can appear in more than one rail/grid
// at once) in sync whenever the underlying set changes, without re-rendering whole cards.
onFavoritesChange(() => {
  document.querySelectorAll("[data-favorite-product-id]").forEach((btn) => {
    const id = Number(btn.dataset.favoriteProductId);
    btn.classList.toggle("active", isFavorited(id));
  });
});

onCompareChange(() => {
  document.querySelectorAll("[data-compare-product-id]").forEach((btn) => {
    const id = Number(btn.dataset.compareProductId);
    btn.classList.toggle("active", isCompared(id));
  });
});

function showSkeletonLoading(container) {
  container.className = "product-grid";
  container.innerHTML = Array(8)
    .fill(
      `<div class="skeleton-card">
        <div class="skeleton-image"></div>
        <div class="skeleton-body">
          <div class="skeleton-line short"></div>
          <div class="skeleton-line long"></div>
          <div class="skeleton-line medium"></div>
        </div>
      </div>`
    )
    .join("");
}

function showEmptyState(container, message) {
  container.className = "product-grid";
  let actionHtml = "";
  if (state.category && state.q) {
    actionHtml = `<button type="button" class="btn-clear-category-search" style="margin-top:14px; padding:9px 18px; background:#2563eb; color:#fff; border:none; border-radius:99px; cursor:pointer; font-size:13px; font-weight:600; box-shadow:0 2px 6px rgba(37,99,235,0.3);">Tüm kategorilerde "${escapeHtml(state.q)}" için ara</button>`;
  }
  container.innerHTML = `
    <div class="state-message">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
        <circle cx="11" cy="11" r="8"/>
        <path d="m21 21-4.35-4.35"/>
      </svg>
      <h3>Ürün bulunamadı</h3>
      <p>${message}</p>
      ${actionHtml}
    </div>`;

  const btn = container.querySelector(".btn-clear-category-search");
  if (btn) {
    btn.addEventListener("click", () => {
      state.category = "";
      const catInput = document.getElementById("categoryFilter");
      if (catInput) catInput.value = "";
      window.history.replaceState({}, "", window.location.pathname + (state.q ? `?q=${encodeURIComponent(state.q)}` : ""));
      loadProducts();
    });
  }
}

function showErrorState(container) {
  container.className = "product-grid";
  container.innerHTML = `
    <div class="state-message">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
        <circle cx="12" cy="12" r="10"/>
        <path d="M12 8v4M12 16h.01"/>
      </svg>
      <h3>Bir hata oluştu</h3>
      <p>Ürünler yüklenemedi. API'nin çalıştığından emin olun.</p>
    </div>`;
}

export function renderProductCard(product, options = {}) {
  const card = document.createElement("article");
  card.className = "product-card";
  card.setAttribute("role", "button");
  card.tabIndex = 0;

  const imageHtml = product.ImageUrl
    ? `<img src="${escapeHtml(product.ImageUrl)}" class="product-image" alt="${escapeHtml(product.CanonicalTitle)}" loading="lazy">`
    : `<svg class="product-image-placeholder" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
         <rect x="3" y="3" width="18" height="18" rx="2"/>
         <circle cx="8.5" cy="8.5" r="1.5"/>
         <path d="m21 15-5-5L5 21"/>
       </svg>`;

  card.innerHTML = `
    <div class="product-card-surface">
      <div class="product-image-wrap">
        ${imageHtml}
        ${product.DiscountPercent ? `<span class="product-discount-badge">%${product.DiscountPercent} daha düşük</span>` : ""}
      </div>
      <div class="product-body">
        ${product.Brand ? `<p class="product-brand">${escapeHtml(product.Brand)}</p>` : ""}
        <h3 class="product-title">${escapeHtml(product.CanonicalTitle)}</h3>
        <div class="product-footer">
          ${
            product.LowestPrice != null
              ? `<div>
                  <span class="product-price-label">En düşük fiyat</span>
                  ${product.DiscountPercent ? `<span class="product-old-price">${formatPrice(product.OldPrice)}</span>` : ""}
                  <span class="product-price">${formatPrice(product.LowestPrice)}</span>
                  ${
                    product.LowestPriceStore
                      ? `<span class="price-store-badge">(${escapeHtml(product.LowestPriceStore)})</span>`
                      : ""
                  }
                  ${formatUnitPrice(product.UnitPrice, product.UnitLabel)}
                </div>`
              : `<div><span class="product-unavailable">Şu anda satışta değil</span></div>`
          }
          <span class="product-stores">${product.OfferCount} mağaza</span>
        </div>
      </div>
    </div>
        </div>
      </div>
    </div>`;

  card.querySelector(".product-image-wrap").appendChild(buildFavoriteButton(product.Id));
  card.querySelector(".product-image-wrap").appendChild(buildCompareButton(product));

  if (options.onRemovePurchase) {
    const removeBtn = document.createElement("button");
    removeBtn.type = "button";
    removeBtn.className = "product-remove-purchase-btn";
    removeBtn.setAttribute("aria-label", "Satın alma geçmişinden kaldır");
    removeBtn.title = "Satın alma geçmişinden kaldır";
    removeBtn.innerHTML = `<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18m-2 0v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6m3 0V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2m-6 5v6m4-6v6"/></svg>`;
    removeBtn.style.cssText = `
      position: absolute;
      top: 8px;
      right: 8px;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: rgba(255, 255, 255, 0.95);
      border: 1px solid #fee2e2;
      color: #ef4444;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      z-index: 10;
      box-shadow: 0 2px 6px rgba(0,0,0,0.08);
      transition: all 0.2s ease;
    `;
    removeBtn.addEventListener("mouseenter", () => {
      removeBtn.style.background = "#fee2e2";
      removeBtn.style.transform = "scale(1.1)";
    });
    removeBtn.addEventListener("mouseleave", () => {
      removeBtn.style.background = "rgba(255, 255, 255, 0.95)";
      removeBtn.style.transform = "scale(1)";
    });
    removeBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      options.onRemovePurchase(product, card);
    });
    card.querySelector(".product-image-wrap").appendChild(removeBtn);
  }

  const open = () => openProductDetail(product.Id, product.CanonicalTitle);
  card.addEventListener("click", open);
  card.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      open();
    }
  });

  return card;
}

// Filter/search/sort params only — no skip/count. Used both to build the real fetch URL (with
// skip+pageSize appended) and, on its own, as a key to detect "did the filters change since a
// background refresh was scheduled" (see scheduleLiveSearchPolls/silentRefresh).
function buildFilterParams() {
  const params = new URLSearchParams();
  if (state.q) params.set("q", state.q);
  if (state.brands && state.brands.length > 0) {
    state.brands.forEach((b) => params.append("brand", b));
  } else if (state.brand) {
    params.set("brand", state.brand);
  }
  if (state.stores && state.stores.length > 0) {
    state.stores.forEach((s) => params.append("store", s));
  }
  if (state.category) params.set("category", state.category);
  if (state.sort) params.set("sort", state.sort);
  if (state.minPrice) params.set("minPrice", state.minPrice);
  if (state.maxPrice) params.set("maxPrice", state.maxPrice);
  if (state.onSale) params.set("onSale", "true");
  state.attrs.forEach((a) => params.append("attr", a));
  return params;
}

function buildQueryParams(skip, count) {
  const params = buildFilterParams();
  params.set("skip", skip);
  params.set("pageSize", count);
  return params.toString();
}

function syncUrlParams() {
  const params = buildFilterParams();
  if (state.page > 1) {
    params.set("page", state.page);
  }
  if (params.get("sort") === "name") {
    params.delete("sort");
  }
  const newSearch = params.toString() ? `?${params.toString()}` : window.location.pathname;
  window.history.replaceState({}, "", newSearch);
}

// Timers for the quiet background re-fetches triggered by liveSearchPending (see below).
// Cleared whenever a fresh loadProducts() call fires so a stale poll never clobbers newer results.
let liveSearchPollTimers = [];
const LIVE_SEARCH_POLL_DELAYS_MS = [4000, 9000, 16000];

function clearLiveSearchPolls() {
  liveSearchPollTimers.forEach((id) => clearTimeout(id));
  liveSearchPollTimers = [];
}

// Re-fetches the current page in the background — no skeleton, no scroll jump — so newly scraped products can appear.
async function silentRefresh(filterKey) {
  if (buildFilterParams().toString() !== filterKey) return;

  try {
    const skip = (state.page - 1) * state.pageSize;
    const query = buildQueryParams(skip, state.pageSize);
    const url = `${API_BASE}/products?${query}`;
    const token = getToken();
    const res = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
    if (!res.ok) return;
    const data = await res.json();

    if (buildFilterParams().toString() !== filterKey) return;

    state.allProducts = data.items || [];
    state.totalCount = data.totalCount || 0;
    renderProductsPage();
  } catch (err) {
    console.error("Live search refresh failed:", err);
  }
}

function scheduleLiveSearchPolls(filterKey) {
  clearLiveSearchPolls();
  liveSearchPollTimers = LIVE_SEARCH_POLL_DELAYS_MS.map((delay) => setTimeout(() => silentRefresh(filterKey), delay));
}

const RECOMMENDED_VIEW_ALL_LIMIT = 30;

export async function loadRecommendedProducts() {
  clearLiveSearchPolls();
  const container = $("products");
  showSkeletonLoading(container);
  hidePagination();
  const breadcrumbEl = $("breadcrumb");
  if (breadcrumbEl) breadcrumbEl.style.display = "none";

  try {
    const token = getToken();
    const res = await fetch(`${API_BASE}/products/recommended?limit=${RECOMMENDED_VIEW_ALL_LIMIT}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    });
    if (!res.ok) throw new Error(`API returned ${res.status}`);
    const { items } = await res.json();

    state.allProducts = items || [];
    state.totalCount = state.allProducts.length;
    renderProductsPage();
  } catch (err) {
    showErrorState(container);
    $("resultsCount").textContent = "";
    hidePagination();
    console.error(err);
  }
}

export async function loadProducts(page = state.page) {
  state.page = page;
  state.recommended = false;
  clearLiveSearchPolls();
  syncUrlParams();

  const container = $("products");
  showSkeletonLoading(container);
  hidePagination();
  renderActiveFilters();

  try {
    const filterKey = buildFilterParams().toString();
    const skip = (state.page - 1) * state.pageSize;
    const url = `${API_BASE}/products?${buildQueryParams(skip, state.pageSize)}`;
    const token = getToken();
    const res = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
    if (!res.ok) {
      throw new Error(`API returned ${res.status}`);
    }
    const data = await res.json();
    state.allProducts = data.items || [];
    state.totalCount = data.totalCount || 0;
    renderProductsPage();

    if (data.liveSearchPending) {
      scheduleLiveSearchPolls(filterKey);
    }
    
    // Breadcrumb logic
    const breadcrumbEl = $("breadcrumb");
    if (state.category) {
      try {
        const bRes = await fetch(`${API_BASE}/categories/${encodeURIComponent(state.category)}/breadcrumb`);
        if (bRes.ok) {
          const breadcrumbs = await bRes.json();
          breadcrumbEl.style.display = "flex";
          const arrowSvg = `<span class="breadcrumb-sep"><svg viewBox="0 0 10 10" fill="none" style="width: 11px; height: 11px;"><path d="M3 2L7 5L3 8" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg></span>`;

          let html = `<button type="button" class="breadcrumb-link" data-slug="" aria-label="Tüm kategorileri göster">Tüm Ürünler</button>${arrowSvg}`;

          html += breadcrumbs.map((b, i) => {
            const isLast = i === breadcrumbs.length - 1;
            const icon = i === 0 ? `<span style="margin-right: 4px;">${getCategoryIcon(b)}</span>` : "";
            const text = escapeHtml(b.Name);
            if (isLast) {
              return `<span class="breadcrumb-current">${icon}${text}</span>`;
            }
            return `<button type="button" class="breadcrumb-link" data-slug="${escapeHtml(b.Slug)}">${icon}${text}</button>${arrowSvg}`;
          }).join("");

          breadcrumbEl.innerHTML = html;

          breadcrumbEl.querySelectorAll(".breadcrumb-link").forEach((btn) => {
            btn.addEventListener("click", () => {
              const targetSlug = btn.dataset.slug;
              selectCategory(targetSlug);
            });
          });
        }
      } catch (err) {
        console.error("Failed to load breadcrumb:", err);
        breadcrumbEl.style.display = "none";
      }
    } else {
      breadcrumbEl.style.display = "none";
    }

  } catch (err) {
    showErrorState(container);
    $("resultsCount").textContent = "";
    hidePagination();
    console.error(err);
  }
}

function hidePagination() {
  const paginationEl = $("pagination");
  if (paginationEl) {
    paginationEl.hidden = true;
    paginationEl.innerHTML = "";
  }
}

function renderPagination() {
  const paginationEl = $("pagination");
  if (!paginationEl) return;

  const totalPages = Math.ceil(state.totalCount / state.pageSize);
  if (totalPages <= 1 || state.allProducts.length === 0) {
    paginationEl.hidden = true;
    paginationEl.innerHTML = "";
    return;
  }

  paginationEl.hidden = false;

  const fromItem = (state.page - 1) * state.pageSize + 1;
  const toItem = Math.min(state.page * state.pageSize, state.totalCount);

  // Generate windowed page numbers
  const pages = getPageWindow(state.page, totalPages);

  let controlsHtml = `
    <button type="button" class="pagination-btn pagination-prev" ${state.page <= 1 ? "disabled" : ""} aria-label="Önceki Sayfa">
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" style="margin-right:2px;"><polyline points="15 18 9 12 15 6"/></svg>
      <span>Önceki</span>
    </button>
  `;

  pages.forEach((p) => {
    if (p === "...") {
      controlsHtml += `<span class="pagination-ellipsis">…</span>`;
    } else {
      const isCurrent = p === state.page;
      controlsHtml += `<button type="button" class="pagination-btn${isCurrent ? " active" : ""}" data-page="${p}" ${isCurrent ? 'aria-current="page"' : ""}>${p}</button>`;
    }
  });

  controlsHtml += `
    <button type="button" class="pagination-btn pagination-next" ${state.page >= totalPages ? "disabled" : ""} aria-label="Sonraki Sayfa">
      <span>Sonraki</span>
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" style="margin-left:2px;"><polyline points="9 18 15 12 9 6"/></svg>
    </button>
  `;

  paginationEl.innerHTML = `
    <div class="pagination-info">
      Toplam <strong>${state.totalCount.toLocaleString("tr-TR")}</strong> üründen <strong>${fromItem} - ${toItem}</strong> arası gösteriliyor (Sayfa ${state.page} / ${totalPages})
    </div>
    <div class="pagination-controls">
      ${controlsHtml}
    </div>
  `;

  // Attach event listeners
  paginationEl.querySelectorAll(".pagination-btn[data-page]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const targetPage = Number(btn.dataset.page);
      if (targetPage !== state.page) {
        goToPage(targetPage);
      }
    });
  });

  const prevBtn = paginationEl.querySelector(".pagination-prev");
  if (prevBtn && state.page > 1) {
    prevBtn.addEventListener("click", () => goToPage(state.page - 1));
  }

  const nextBtn = paginationEl.querySelector(".pagination-next");
  if (nextBtn && state.page < totalPages) {
    nextBtn.addEventListener("click", () => goToPage(state.page + 1));
  }
}

function goToPage(targetPage) {
  loadProducts(targetPage);
  const scrollTarget = document.querySelector(".products-section") || $("products");
  if (scrollTarget) {
    scrollTarget.scrollIntoView({ behavior: "smooth", block: "start" });
  }
}

function getPageWindow(current, total) {
  if (total <= 7) {
    return Array.from({ length: total }, (_, i) => i + 1);
  }

  if (current <= 4) {
    return [1, 2, 3, 4, 5, "...", total];
  }

  if (current >= total - 3) {
    return [1, "...", total - 4, total - 3, total - 2, total - 1, total];
  }

  return [1, "...", current - 1, current, current + 1, "...", total];
}

// Renders the products for the current page
function renderProductsPage() {
  const container = $("products");
  const products = state.allProducts;

  container.className = "product-grid";
  container.innerHTML = "";

  $("resultsCount").textContent = state.recommended
    ? state.totalCount === 0
      ? "Henüz senin için bir öneri yok"
      : `Senin için ${state.totalCount} önerilen ürün`
    : state.totalCount === 0
      ? "Sonuç bulunamadı"
      : `${state.totalCount.toLocaleString("tr-TR")} ürün bulundu`;

  if (products.length === 0) {
    hidePagination();
    showEmptyState(
      container,
      state.recommended
        ? "Arama geçmişin arttıkça burada sana özel öneriler görünecek."
        : state.q || state.brand || state.category || state.minPrice || state.maxPrice
          ? "Filtreleri değiştirmeyi veya aramayı temizlemeyi deneyin."
          : "Henüz ürün eklenmemiş."
    );
    return;
  }

  products.forEach((p) => container.appendChild(renderProductCard(p)));
  renderPagination();
}

