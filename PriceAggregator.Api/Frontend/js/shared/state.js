export const state = {
  q: "",
  brand: "", // legacy single string
  brands: [], // multi-select brands: ["Ülker", "Eti", ...]
  stores: [], // multi-select stores: ["migros", "teknosa", ...]
  category: "",
  sort: "name",
  minPrice: "",
  maxPrice: "",
  attrs: [], // ["Renk:Bej", "Özellik:Organik", ...]
  onSale: false,
  recommended: false,
  allProducts: [],
  totalCount: 0,
  page: 1,
  pageSize: 24,
};

export const DEFAULT_PAGE_SIZE = 24;
export const INITIAL_PAGE_SIZE = 24;
export const SCROLL_BATCH_SIZE = 24;

// Mutated in place (categories.length = 0; categories.push(...)) rather than reassigned,
// so every module that imports this binding keeps seeing the live list — see
// categoryMenu.js's loadCategories.
export const categories = [];

