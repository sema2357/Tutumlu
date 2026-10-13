// Client-side cache of the current user's favorited product ids, kept in sync with the
// server so every rendered product card (home rails, products grid, account favorites tab)
// can show the right heart state without each one fetching /favorites itself.
import { getSession, onAuthChange, apiFetch } from "./auth.js";

let favoriteIds = new Set();
const listeners = new Set();

function notify() {
  listeners.forEach((fn) => fn());
}

export function isFavorited(productId) {
  return favoriteIds.has(productId);
}

// Called whenever the favorited set changes (login/logout, or a toggle) so rendered
// heart buttons can resync — see productGrid.js.
export function onFavoritesChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

async function refreshFavoriteIds() {
  if (!getSession()) {
    favoriteIds = new Set();
    notify();
    return;
  }
  try {
    const items = await apiFetch("/favorites");
    favoriteIds = new Set(items.map((p) => p.Id));
  } catch {
    favoriteIds = new Set();
  }
  notify();
}

// Optimistic: flips locally and notifies immediately, then confirms with the API —
// reverts and notifies again if the request fails.
export async function toggleFavorite(productId) {
  const wasFavorited = favoriteIds.has(productId);
  if (wasFavorited) favoriteIds.delete(productId);
  else favoriteIds.add(productId);
  notify();

  try {
    await apiFetch(`/favorites/${productId}`, { method: wasFavorited ? "DELETE" : "POST" });
    // Confirms the optimistic state once the server call has actually committed — matters
    // for listeners that refetch a server-backed list (e.g. the account page's favorites
    // tab), since a refetch right after the earlier optimistic notify could otherwise race
    // ahead of the request it's meant to reflect.
    notify();
  } catch (err) {
    if (wasFavorited) favoriteIds.add(productId);
    else favoriteIds.delete(productId);
    notify();
    throw err;
  }
}

onAuthChange(refreshFavoriteIds);
refreshFavoriteIds();
