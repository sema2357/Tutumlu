import { $, escapeHtml } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { state, categories } from "../shared/state.js";

function buildCategoryChildrenMap() {
  const map = new Map();
  categories.forEach((c) => {
    const key = c.ParentCategoryId ?? "root";
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(c);
  });
  return map;
}

const DEFAULT_CATEGORY_ICON = '<i class="fa fa-cube"></i>';

export function getCategoryIcon(category) {
  if (!category) return DEFAULT_CATEGORY_ICON;
  
  const icon = category.IconClass || category.IconSlug;
  if (!icon) return DEFAULT_CATEGORY_ICON;

  if (icon.startsWith("fa ") || icon.startsWith("fa-")) {
    return `<i class="${escapeHtml(icon)}"></i>`;
  }
  
  return escapeHtml(icon);
}

// 4-level mega menu state: Level-0 (sector, e.g. Market/Mobilya) drives which Level-1
// (category) list shows, which drives Level-2 (sub), which drives what Level-3 (item)
// column shows.
let activeSectorId = null;
let activeRootId = null;
let activeSubId = null;

// Walks from `cat` up to its sector root (Market/Mobilya — the real top of the tree),
// returning [sectorRoot, ...ancestors..., cat].
export function getCategoryChain(cat) {
  const chain = [cat];
  let current = cat;
  while (current.ParentCategoryId != null) {
    const parent = categories.find((c) => c.Id === current.ParentCategoryId);
    if (!parent) break;
    chain.unshift(parent);
    current = parent;
  }
  return chain;
}

function initMegaMenuActiveFromSelection() {
  if (!state.category) return;
  const selected = categories.find((c) => c.Slug === state.category);
  if (!selected) return;

  const chain = getCategoryChain(selected);
  // chain[0] is always the sector root, even when selected IS the sector root itself
  // (chain.length === 1) — e.g. products CategoryResolver couldn't classify past the
  // sector level (see CategoryClassifier's null-match fallback).
  activeSectorId = chain[0].Id;
  activeRootId = chain.length >= 2 ? chain[1].Id : null;
  activeSubId = chain.length >= 3 ? chain[2].Id : null;
}

function renderMegaMenu() {
  const byParent = buildCategoryChildrenMap();

  // Resolve the active path top-down: Level 0 (sector) -> Level 1 -> Level 2, each
  // constrained to be a real child of the previous one with active products (ProductCount > 0).
  const sectors = (byParent.get("root") || [])
    .filter((s) => (s.ProductCount || 0) > 0)
    .slice()
    .sort((a, b) => a.Name.localeCompare(b.Name, "tr"));

  if ((activeSectorId == null || !sectors.some((s) => s.Id === activeSectorId)) && sectors.length) {
    activeSectorId = sectors[0].Id;
  }
  const currentSector = sectors.find((s) => s.Id === activeSectorId) ?? null;

  const roots = currentSector
    ? (byParent.get(currentSector.Id) || [])
        .filter((r) => (r.ProductCount || 0) > 0)
        .slice()
        .sort((a, b) => a.Name.localeCompare(b.Name, "tr"))
    : [];

  if ((activeRootId == null || !roots.some((r) => r.Id === activeRootId)) && roots.length) {
    activeRootId = roots[0].Id;
  }
  const currentRoot = roots.find((r) => r.Id === activeRootId) ?? null;

  const subs = currentRoot
    ? (byParent.get(currentRoot.Id) || [])
        .filter((s) => (s.ProductCount || 0) > 0)
        .slice()
        .sort((a, b) => a.Name.localeCompare(b.Name, "tr"))
    : [];
  if (activeSubId != null && !subs.some((s) => s.Id === activeSubId)) activeSubId = null;
  const currentSub = subs.find((s) => s.Id === activeSubId) ?? null;

  const items = currentSub
    ? (byParent.get(currentSub.Id) || [])
        .filter((i) => (i.ProductCount || 0) > 0)
        .slice()
        .sort((a, b) => a.Name.localeCompare(b.Name, "tr"))
    : [];

  // Level 0
  const col0 = document.querySelector("#categoryMegaMenu .mega-col-0");
  col0.innerHTML = "";
  sectors.forEach((sector) => {
    const li = document.createElement("li");
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "mega-l0-item" + (sector.Id === activeSectorId ? " active" : "");
    btn.dataset.slug = sector.Slug;
    btn.innerHTML = `
      <span class="mega-l0-icon">${getCategoryIcon(sector)}</span>
      <span class="mega-l0-label">${escapeHtml(sector.Name)} <span class="mega-count">(${sector.ProductCount.toLocaleString("tr-TR")})</span></span>`;
    btn.addEventListener("mouseenter", () => {
      if (activeSectorId !== sector.Id) {
        activeSectorId = sector.Id;
        activeRootId = null;
        activeSubId = null;
        renderMegaMenu();
      }
    });
    btn.addEventListener("click", () => selectCategory(sector.Slug));
    li.appendChild(btn);
    col0.appendChild(li);
  });

  // Level 1
  const col1 = document.querySelector("#categoryMegaMenu .mega-col-1");
  col1.innerHTML = "";
  roots.forEach((root) => {
    const li = document.createElement("li");
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "mega-l1-item" + (root.Id === activeRootId ? " active" : "");
    btn.dataset.slug = root.Slug;
    btn.innerHTML = `
      <span class="mega-l1-icon">${getCategoryIcon(root)}</span>
      <span class="mega-l1-label">${escapeHtml(root.Name)} <span class="mega-count">(${root.ProductCount.toLocaleString("tr-TR")})</span></span>
      <svg class="mega-l1-arrow" viewBox="0 0 12 12" fill="none"><path d="M4 2.5L8 6L4 9.5" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
    btn.addEventListener("mouseenter", () => {
      if (activeRootId !== root.Id) {
        activeRootId = root.Id;
        activeSubId = null;
        renderMegaMenu();
      }
    });
    btn.addEventListener("click", () => selectCategory(root.Slug));
    li.appendChild(btn);
    col1.appendChild(li);
  });

  // Level 2
  document.querySelector("#categoryMegaMenu .mega-col-2-title").textContent = currentRoot ? currentRoot.Name : "";
  const col2List = document.querySelector("#categoryMegaMenu .mega-col-2-list");
  col2List.innerHTML = "";
  subs.forEach((sub) => {
    const childCount = (byParent.get(sub.Id) || []).filter((c) => (c.ProductCount || 0) > 0).length;
    const li = document.createElement("li");
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "mega-l2-item" + (sub.Id === activeSubId ? " active" : "");
    btn.dataset.slug = sub.Slug;
    btn.innerHTML = `
      <span class="mega-l2-label">${escapeHtml(sub.Name)} <span class="mega-count">(${sub.ProductCount.toLocaleString("tr-TR")})</span></span>
      <span class="mega-l2-meta">
        ${childCount ? `<span class="mega-l2-count">${childCount} alt kategori</span>` : ""}
        <svg viewBox="0 0 10 10" fill="none"><path d="M3 2L7 5L3 8" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>
      </span>`;
    btn.addEventListener("mouseenter", () => {
      if (activeSubId !== sub.Id) {
        activeSubId = sub.Id;
        renderMegaMenu();
      }
    });
    btn.addEventListener("click", () => selectCategory(sub.Slug));
    li.appendChild(btn);
    col2List.appendChild(li);
  });

  // Level 3
  const col3 = document.querySelector("#categoryMegaMenu .mega-col-3");
  col3.innerHTML = "";
  if (currentSub) {
    const breadcrumb = document.createElement("div");
    breadcrumb.className = "mega-breadcrumb";
    breadcrumb.innerHTML = `
      <span>${getCategoryIcon(currentRoot)}</span>
      <span>${escapeHtml(currentRoot.Name)}</span>
      <svg viewBox="0 0 10 10" fill="none"><path d="M3 2L7 5L3 8" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>
      <span class="mega-breadcrumb-current">${escapeHtml(currentSub.Name)}</span>`;
    col3.appendChild(breadcrumb);

    const title = document.createElement("p");
    title.className = "mega-col-3-title";
    title.textContent = `${currentSub.Name} ürünleri`;
    col3.appendChild(title);

    // If there are sub-items with products, show them; otherwise show the subcategory itself if it has products.
    const displayItems = items.length > 0 ? items : (currentSub.ProductCount > 0 ? [currentSub] : []);

    const grid = document.createElement("ul");
    grid.className = "mega-item-grid";
    displayItems.forEach((item) => {
      const li = document.createElement("li");
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "mega-l3-item";
      btn.dataset.slug = item.Slug;
      btn.innerHTML = `<span class="mega-l3-dot"></span>${escapeHtml(item.Name)} <span class="mega-count" style="margin-left: 4px; color: #888; font-size: 0.9em;">(${item.ProductCount.toLocaleString("tr-TR")})</span>`;
      btn.addEventListener("click", () => selectCategory(item.Slug));
      li.appendChild(btn);
      grid.appendChild(li);
    });
    col3.appendChild(grid);
  } else {
    const empty = document.createElement("div");
    empty.className = "mega-empty-state";
    empty.innerHTML = `
      <span class="mega-empty-icon">${currentRoot ? getCategoryIcon(currentRoot) : DEFAULT_CATEGORY_ICON}</span>
      <p>Bir alt kategori seçin</p>`;
    col3.appendChild(empty);
  }

  syncCategorySelectionHighlight();
}

// Homepage icon rail, one card per sector (Level 0) — the same real category list the
// mega menu's first column uses, so clicking a card here and picking the sector in the
// menu land on the exact same filtered results.
function renderCategoryRail() {
  const rail = $("categoryRail");
  if (!rail) return;

  const byParent = buildCategoryChildrenMap();
  const sectors = (byParent.get("root") || [])
    .filter((s) => (s.ProductCount || 0) > 0)
    .slice()
    .sort((a, b) => a.Name.localeCompare(b.Name, "tr"));

  rail.innerHTML = "";
  sectors.forEach((sector) => {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "category-rail-item";
    btn.innerHTML = `
      <span class="category-rail-icon">${getCategoryIcon(sector)}</span>
      <span class="category-rail-label">${escapeHtml(sector.Name)} <small style="display:block;color:#888;">(${sector.ProductCount.toLocaleString("tr-TR")})</small></span>`;
    btn.addEventListener("click", () => selectCategory(sector.Slug));
    rail.appendChild(btn);
  });
}

// What happens once a category slug is picked — set by whichever page called
// initCategoryMenu (products.js filters in place; home.js navigates to products.html).
let onCategorySelected = () => {};

export function selectCategory(slug) {
  state.category = slug;
  closeCategoryMenu();
  syncCategorySelectionHighlight();
  onCategorySelected(slug);
}

export function syncCategorySelectionHighlight() {
  document
    .querySelectorAll(".mega-l0-item, .mega-l1-item, .mega-l2-item, .mega-l3-item")
    .forEach((el) => el.classList.toggle("selected", el.dataset.slug === state.category));
}

export function openCategoryMenu() {
  initMegaMenuActiveFromSelection();
  renderMegaMenu();
  $("categoryMegaMenu").classList.add("open");
  $("categoryMenuTrigger").classList.add("open");
}

export function closeCategoryMenu() {
  $("categoryMegaMenu").classList.remove("open");
  $("categoryMenuTrigger").classList.remove("open");
}

export async function loadCategories() {
  try {
    const res = await fetch(`${API_BASE}/categories`);
    const data = await res.json();
    // Mutated in place, not reassigned, so the `categories` binding stays live for every
    // module that imported it from state.js — see state.js.
    categories.length = 0;
    categories.push(...data);
    renderMegaMenu();
    renderCategoryRail();
  } catch (err) {
    console.error("Failed to load categories:", err);
  }
}

export function initCategoryMenu(onSelect) {
  onCategorySelected = onSelect;

  $("categoryMenuTrigger").addEventListener("click", () => {
    if ($("categoryMegaMenu").classList.contains("open")) closeCategoryMenu();
    else openCategoryMenu();
  });

  document.addEventListener("click", (e) => {
    const menu = $("categoryMegaMenu");
    const trigger = $("categoryMenuTrigger");
    if (!menu.contains(e.target) && !trigger.contains(e.target)) {
      closeCategoryMenu();
    }
  });
}
