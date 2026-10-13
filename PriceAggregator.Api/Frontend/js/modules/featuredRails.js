import { $ } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { getToken } from "../shared/auth.js";
import { renderProductCard } from "./productGrid.js";

const RAIL_LIMIT = 10;

// Hits the personalized /api/products/recommended endpoint (auth header attached when the
// shopper is logged in; falls back server-side to a popularity list for guests/shoppers
// without enough interaction history yet — see ProductEndpoints.cs). Used for both homepage
// rails now: onSale=true reuses the exact same personalization/round-robin logic, just scoped
// to products with an active discount, so "Kampanyalı ürünler" stops always showing the same
// alphabetically-first discounted products to every shopper.
async function loadRecommendedRail(sectionId, containerId, onSale) {
  const section = $(sectionId);
  const container = $(containerId);
  if (!section || !container) return;

  try {
    const token = getToken();
    const params = `limit=${RAIL_LIMIT}${onSale ? "&onSale=true" : ""}`;
    const res = await fetch(`${API_BASE}/products/recommended?${params}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    });
    if (!res.ok) throw new Error(`API returned ${res.status}`);
    const { items } = await res.json();

    if (!items || items.length === 0) {
      section.hidden = true;
      return;
    }

    container.innerHTML = "";
    items.forEach((p) => container.appendChild(renderProductCard(p)));
    section.hidden = false;
  } catch (err) {
    section.hidden = true;
    console.error(`Failed to load rail ${containerId}:`, err);
  }
}

// Click a rail's prev/next arrow to scroll by roughly one screenful of cards, rather than
// relying on drag/trackpad scrolling alone.
function initRailNav() {
  document.querySelectorAll("[data-rail-prev], [data-rail-next]").forEach((btn) => {
    const targetId = btn.dataset.railPrev || btn.dataset.railNext;
    const direction = btn.dataset.railPrev ? -1 : 1;
    btn.addEventListener("click", () => {
      const track = $(targetId);
      track?.scrollBy({ left: direction * track.clientWidth * 0.9, behavior: "smooth" });
    });
  });
}

export function loadFeaturedRails() {
  initRailNav();
  loadRecommendedRail("onSaleRail", "onSaleProducts", true);
  loadRecommendedRail("popularRail", "popularProducts", false);
}
