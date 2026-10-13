// Client-side (no backend) selection of up to 4 products to compare side by side.
// Persisted to sessionStorage so it survives the products.html -> compare.html navigation
// but resets when the tab closes — a comparison list is a throwaway shopping-session thing,
// not something worth syncing across devices like favorites.js does.
const STORAGE_KEY = "compareItems";
export const MAX_COMPARE_ITEMS = 4;

let items = loadFromStorage();
const listeners = new Set();
const limitListeners = new Set();

function loadFromStorage() {
  try {
    const parsed = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? "[]");
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function saveToStorage() {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(items));
  } catch {
    // Storage full/unavailable — selection just won't survive a reload.
  }
}

function notify() {
  listeners.forEach((fn) => fn());
}

export function getCompareItems() {
  return items;
}

export function isCompared(productId) {
  return items.some((p) => p.id === productId);
}

// Called whenever the selection changes (add/remove/clear) — see productGrid.js's compare
// button and compareBar.js's floating bar.
export function onCompareChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

// Fired when toggleCompare rejects an add because MAX_COMPARE_ITEMS is already selected —
// separate from onCompareChange since the selection itself doesn't change in that case.
export function onCompareLimitReached(fn) {
  limitListeners.add(fn);
  return () => limitListeners.delete(fn);
}

// product: { id, title, imageUrl }. Returns false without changing anything if the product
// isn't already selected and the list is already at MAX_COMPARE_ITEMS.
export function toggleCompare(product) {
  const exists = items.some((p) => p.id === product.id);
  if (exists) {
    items = items.filter((p) => p.id !== product.id);
  } else {
    if (items.length >= MAX_COMPARE_ITEMS) {
      limitListeners.forEach((fn) => fn());
      return false;
    }
    items = [...items, { id: product.id, title: product.title, imageUrl: product.imageUrl }];
  }
  saveToStorage();
  notify();
  return true;
}

export function removeFromCompare(productId) {
  items = items.filter((p) => p.id !== productId);
  saveToStorage();
  notify();
}

export function clearCompare() {
  items = [];
  saveToStorage();
  notify();
}

// Browsers restoring a page from the back/forward cache (e.g. hitting "back" from
// compare.html to products.html) resume the page's JS with its in-memory state frozen as it
// was, rather than re-running the module — so `items` could still show the old selection
// even though compare.js already cleared sessionStorage. Resyncing on pageshow (which fires
// on every visit, bfcache or not) keeps card/button state correct either way.
window.addEventListener("pageshow", () => {
  items = loadFromStorage();
  notify();
});
