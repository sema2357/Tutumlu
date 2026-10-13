import { $, escapeHtml } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { getToken } from "../shared/auth.js";
import { openProductDetail } from "./modal.js";
import { getCategoryIcon, selectCategory } from "./categoryMenu.js";

// Type-ahead dropdown under the search bar: as the shopper types, shows matching
// categories/brands/products; when focused with empty input, shows the shopper's own
// recent searches (logged-in shoppers only — nothing is shown for guests or shoppers
// without history yet).
let searchSuggestionsDebounce = null;
let cachedPersonalHistory = null;

// Fire-and-forget: records a direct pick (product or brand) from the dropdown as a search
// event, same as a typed-and-submitted query, so it feeds personal suggestions/recommendations.
// Invalidates the cache so the next time the box is focused it picks up this new entry.
function logSearchHistoryPick({ term, productId, brand }) {
  const token = getToken();
  if (!token) return;

  cachedPersonalHistory = null;
  fetch(`${API_BASE}/search-history/log`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`
    },
    body: JSON.stringify({ Term: term, ProductId: productId ?? null, Brand: brand ?? null })
  }).catch((err) => console.error("Failed to log search history pick:", err));
}

// What happens on a search submit / brand-suggestion pick — set by whichever page called
// initSearchBox (products.js filters in place; home.js navigates to products.html).
let onSubmit = () => {};
let onBrandPick = () => {};

function closeSearchSuggestions() {
  $("searchSuggestions").innerHTML = "";
  $("searchSuggestions").classList.remove("open");
}

function renderPersonalHistory(terms) {
  const container = $("searchSuggestions");
  container.innerHTML = "";

  if (!terms || terms.length === 0) {
    closeSearchSuggestions();
    return;
  }

  const section = document.createElement("div");
  section.className = "search-suggestions-section search-trending-section";
  section.innerHTML = `
    <div class="search-history-header">
      <p class="search-suggestions-label">🕘 SON ARAMALARIN</p>
      <button type="button" class="search-history-clear">Temizle</button>
    </div>`;

  section.querySelector(".search-history-clear").addEventListener("click", async (e) => {
    e.stopPropagation();
    closeSearchSuggestions();
    cachedPersonalHistory = [];
    try {
      await fetch(`${API_BASE}/search-history`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${getToken()}` }
      });
    } catch (err) {
      console.error("Failed to clear search history:", err);
    }
  });

  const chipsWrap = document.createElement("div");
  chipsWrap.className = "search-trending-chips";

  terms.forEach((term) => {
    if (!term) return;

    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "search-trending-chip";
    btn.innerHTML = `<span class="trending-icon">🕘</span><span class="trending-text">${escapeHtml(term)}</span>`;
    btn.addEventListener("click", () => {
      closeSearchSuggestions();
      $("searchInput").value = term;
      onSubmit(term);
    });
    chipsWrap.appendChild(btn);
  });

  section.appendChild(chipsWrap);
  container.appendChild(section);
  container.classList.add("open");
}

// Shows the shopper's own recent searches when focused with empty input. Guests and
// logged-in shoppers without history yet simply get no suggestions here.
async function loadSearchBoxSuggestions() {
  const term = $("searchInput").value.trim();
  if (term.length >= 2) return;

  const token = getToken();
  if (!token) {
    closeSearchSuggestions();
    return;
  }

  if (cachedPersonalHistory === null) {
    try {
      const res = await fetch(`${API_BASE}/search-history`, {
        headers: { Authorization: `Bearer ${token}` }
      });
      cachedPersonalHistory = res.ok ? await res.json() : [];
    } catch (err) {
      console.error("Failed to load personal search history:", err);
      cachedPersonalHistory = [];
    }
  }

  if ($("searchInput").value.trim().length < 2) {
    renderPersonalHistory(cachedPersonalHistory);
  }
}

function renderSearchSuggestions(data) {
  const container = $("searchSuggestions");
  container.innerHTML = "";

  const hasAny = (data.Categories && data.Categories.length > 0) || data.Brands.length > 0 || data.Products.length > 0;
  if (!hasAny) {
    closeSearchSuggestions();
    return;
  }

  if (data.Categories && data.Categories.length > 0) {
    const section = document.createElement("div");
    section.className = "search-suggestions-section";
    section.innerHTML = `<p class="search-suggestions-label">Kategoriler</p>`;
    data.Categories.forEach((cat) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "search-suggestion-item";
      btn.innerHTML = `<span class="search-suggestion-icon" style="margin-right: 8px;">${getCategoryIcon(cat)}</span> ${escapeHtml(cat.Name)} <span class="search-suggestion-tag" style="background:#eef2ff; color:#4f46e5;">Kategori</span>`;
      btn.addEventListener("click", () => {
        closeSearchSuggestions();
        selectCategory(cat.Slug);
      });
      section.appendChild(btn);
    });
    container.appendChild(section);
  }

  if (data.Brands.length > 0) {
    const section = document.createElement("div");
    section.className = "search-suggestions-section";
    section.innerHTML = `<p class="search-suggestions-label">Markalar</p>`;
    data.Brands.forEach((brand) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "search-suggestion-item";
      btn.innerHTML = `${escapeHtml(brand)} <span class="search-suggestion-tag">Marka</span>`;
      btn.addEventListener("click", () => {
        closeSearchSuggestions();
        logSearchHistoryPick({ term: brand, brand });
        onBrandPick(brand);
      });
      section.appendChild(btn);
    });
    container.appendChild(section);
  }

  if (data.Products.length > 0) {
    const section = document.createElement("div");
    section.className = "search-suggestions-section";
    section.innerHTML = `<p class="search-suggestions-label">Önerilen Ürünler</p>`;
    data.Products.forEach((p) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "search-suggestion-product";
      const imageHtml = p.ImageUrl
        ? `<img src="${escapeHtml(p.ImageUrl)}" alt="" loading="lazy">`
        : `<span class="search-suggestion-product-placeholder"></span>`;
      btn.innerHTML = `${imageHtml}<span>${escapeHtml(p.CanonicalTitle)}</span>`;
      btn.addEventListener("click", () => {
        closeSearchSuggestions();
        logSearchHistoryPick({ term: p.CanonicalTitle, productId: p.Id });
        openProductDetail(p.Id, p.CanonicalTitle);
      });
      section.appendChild(btn);
    });
    container.appendChild(section);
  }

  container.classList.add("open");
}

async function loadSearchSuggestions(term) {
  try {
    const res = await fetch(`${API_BASE}/search-suggestions?q=${encodeURIComponent(term)}`);
    const data = await res.json();
    // The input may have changed (or been cleared) while this request was in flight.
    if ($("searchInput").value.trim() === term) renderSearchSuggestions(data);
  } catch (err) {
    console.error("Failed to load search suggestions:", err);
  }
}

export function initSearchBox(handlers) {
  onSubmit = handlers.onSubmit;
  onBrandPick = handlers.onBrandPick;

  $("searchForm").addEventListener("submit", (e) => {
    e.preventDefault();
    closeSearchSuggestions();
    onSubmit($("searchInput").value.trim());
  });

  $("searchInput").addEventListener("focus", () => {
    const term = $("searchInput").value.trim();
    if (term.length < 2) {
      loadSearchBoxSuggestions();
    }
  });

  $("searchInput").addEventListener("click", () => {
    const term = $("searchInput").value.trim();
    if (term.length < 2) {
      loadSearchBoxSuggestions();
    }
  });

  $("searchInput").addEventListener("input", () => {
    const term = $("searchInput").value.trim();
    clearTimeout(searchSuggestionsDebounce);

    if (term.length < 2) {
      loadSearchBoxSuggestions();
      return;
    }

    searchSuggestionsDebounce = setTimeout(() => loadSearchSuggestions(term), 250);
  });

  $("searchInput").addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeSearchSuggestions();
  });

  document.addEventListener("click", (e) => {
    const wrap = document.querySelector(".search-bar-wrap");
    if (!wrap.contains(e.target)) closeSearchSuggestions();
  });
}
