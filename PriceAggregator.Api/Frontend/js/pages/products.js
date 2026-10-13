// Entry point for products.html — filter panel + product grid. Reads ?category=/?q=/
// ?brand=/?onSale= from the URL on load (set by index.html's navigation links) so landing
// here from the homepage arrives pre-filtered.
import { $ } from "../shared/utils.js";
import { state } from "../shared/state.js";
import {
  initFilters,
  readFilterInputs,
  syncFilterInputs,
  refreshFilterPanels,
} from "../modules/filters.js";
import { initCategoryMenu, loadCategories, closeCategoryMenu, syncCategorySelectionHighlight } from "../modules/categoryMenu.js";
import { initModal, closeModal } from "../modules/modal.js";
import { initLoginModal, closeLoginModal, closeLogoutModal } from "../modules/loginModal.js";
import { initNotificationModal } from "../modules/notificationModal.js";
import { initPurchaseNotifier } from "../shared/purchaseNotifier.js";
import { initSearchBox } from "../modules/search.js";
import { loadProducts, loadRecommendedProducts } from "../modules/productGrid.js";
import { initCompareBar } from "../modules/compareBar.js";

function applyUrlParams() {
  const params = new URLSearchParams(window.location.search);
  state.category = params.get("category") || "";
  state.q = params.get("q") || "";
  const brandParams = params.getAll("brand");
  state.brands = brandParams.length > 0 ? brandParams : (params.get("brand") ? [params.get("brand")] : []);
  state.brand = state.brands.length === 1 ? state.brands[0] : "";
  state.stores = params.getAll("store");
  state.onSale = params.get("onSale") === "true";
  state.recommended = params.get("recommended") === "1";
  const pageParam = parseInt(params.get("page") || "1", 10);
  state.page = isNaN(pageParam) || pageParam < 1 ? 1 : pageParam;
  syncFilterInputs();
}

initFilters();
initCategoryMenu((slug) => {
  $("searchInput").value = "";
  state.q = "";
  state.brand = "";
  state.brands = [];
  state.stores = [];
  state.attrs = [];
  state.minPrice = null;
  state.maxPrice = null;
  state.onSale = false;
  state.recommended = false;
  state.page = 1;
  syncFilterInputs();
  refreshFilterPanels();
  loadProducts(1);
});
initModal();
initLoginModal();
initCompareBar();
initNotificationModal();
initPurchaseNotifier();
initSearchBox({
  onSubmit: (term) => {
    const query = (term !== undefined ? term : $("searchInput").value).trim();
    state.page = 1;
    state.q = query;
    $("searchInput").value = query;
    state.category = "";
    state.brand = "";
    state.brands = [];
    state.stores = [];
    state.attrs = [];
    state.minPrice = "";
    state.maxPrice = "";
    state.onSale = false;
    state.recommended = false;
    syncFilterInputs();
    syncCategorySelectionHighlight();
    refreshFilterPanels();
    loadProducts(1);
  },
  onBrandPick: (brand) => {
    $("searchInput").value = "";
    state.q = "";
    state.brand = brand;
    state.brands = brand ? [brand] : [];
    state.stores = [];
    state.category = "";
    state.attrs = [];
    state.minPrice = "";
    state.maxPrice = "";
    state.onSale = false;
    state.recommended = false;
    state.page = 1;
    syncCategorySelectionHighlight();
    syncFilterInputs();
    refreshFilterPanels();
    loadProducts(1);
  },
});

window.addEventListener("popstate", () => {
  applyUrlParams();
  refreshFilterPanels();
  loadProducts(state.page);
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    closeCategoryMenu();
    closeModal();
    closeLoginModal();
    closeLogoutModal();
  }
});

async function initPage() {
  applyUrlParams();
  await loadCategories();
  refreshFilterPanels();
  if (state.recommended) {
    loadRecommendedProducts();
  } else {
    loadProducts(state.page);
  }
}

initPage();
