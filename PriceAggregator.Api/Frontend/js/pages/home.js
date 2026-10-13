// Entry point for index.html — homepage only (hero, category rail, featured rails).
// Picking a category or searching here navigates to products.html instead of filtering
// in place, since this page has no filter panel/product grid of its own.
import { initCategoryMenu, loadCategories, closeCategoryMenu } from "../modules/categoryMenu.js";
import { initModal, closeModal } from "../modules/modal.js";
import { initLoginModal, closeLoginModal, closeLogoutModal } from "../modules/loginModal.js";
import { initNotificationModal } from "../modules/notificationModal.js";
import { initPurchaseNotifier } from "../shared/purchaseNotifier.js";
import { initSearchBox } from "../modules/search.js";
import { initHero } from "../modules/hero.js";
import { loadFeaturedRails } from "../modules/featuredRails.js";
import { initCompareBar } from "../modules/compareBar.js";

function goToProducts(params) {
  const qs = new URLSearchParams(params).toString();
  window.location.href = qs ? `products.html?${qs}` : "products.html";
}

initCategoryMenu((slug) => goToProducts({ category: slug }));
initModal();
initLoginModal();
initCompareBar();
initNotificationModal();
initPurchaseNotifier();
initSearchBox({
  onSubmit: (term) => {
    if (term) goToProducts({ q: term });
  },
  onBrandPick: (brand) => goToProducts({ brand }),
});
initHero();

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    closeCategoryMenu();
    closeModal();
    closeLoginModal();
    closeLogoutModal();
  }
});

loadCategories();
loadFeaturedRails();
