// Entry point for account.html — "Hesabım": favorites tab + editable profile tab.
// Header/search/category-menu wiring mirrors home.js (no in-page filtering, search just
// navigates to products.html like the homepage does).
import { $, formatPrice, isValidEmail, isValidPhone, populateCitySelect } from "../shared/utils.js";
import { initCategoryMenu, loadCategories, closeCategoryMenu } from "../modules/categoryMenu.js";
import { initModal, closeModal } from "../modules/modal.js";
import { initLoginModal, openLoginModal, openLogoutModal, closeLoginModal, closeLogoutModal } from "../modules/loginModal.js";
import { initNotificationModal } from "../modules/notificationModal.js";
import { initPurchaseNotifier } from "../shared/purchaseNotifier.js";
import { initSearchBox } from "../modules/search.js";
import { renderProductCard } from "../modules/productGrid.js";
import { initCompareBar } from "../modules/compareBar.js";
import { onFavoritesChange } from "../shared/favorites.js";
import { getSession, setSession, onAuthChange, apiFetch } from "../shared/auth.js";

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

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    closeCategoryMenu();
    closeModal();
    closeLoginModal();
    closeLogoutModal();
  }
});

loadCategories();

/* ── Tabs ── */
function setActiveTab(tab) {
  $("tabFavorites").classList.toggle("active", tab === "favorites");
  if ($("tabPurchaseHistory")) $("tabPurchaseHistory").classList.toggle("active", tab === "purchase-history");
  $("tabProfile").classList.toggle("active", tab === "profile");
  $("favoritesPanel").classList.toggle("hidden", tab !== "favorites");
  if ($("purchaseHistoryPanel")) $("purchaseHistoryPanel").classList.toggle("hidden", tab !== "purchase-history");
  $("profilePanel").classList.toggle("hidden", tab !== "profile");

  if (tab === "purchase-history" && !purchaseHistoryLoaded) {
    purchaseHistoryLoaded = true;
    loadPurchaseHistory();
  }
}
$("tabFavorites").addEventListener("click", () => setActiveTab("favorites"));
if ($("tabPurchaseHistory")) $("tabPurchaseHistory").addEventListener("click", () => setActiveTab("purchase-history"));
$("tabProfile").addEventListener("click", () => setActiveTab("profile"));

/* ── Favorites ── */
let favoritesLoaded = false;

// A favorite toggle fires onFavoritesChange twice (optimistic, then post-confirmation —
// see favorites.js), each triggering a reload here. The two /favorites requests can
// resolve out of order, so without this guard a slower, now-stale response can overwrite
// the grid after a newer one already rendered — e.g. re-showing an item right after it was
// unfavorited. Only the most recently *initiated* request is allowed to render.
let favoritesRequestId = 0;

async function loadFavorites() {
  const requestId = ++favoritesRequestId;
  try {
    const items = await apiFetch("/favorites");
    if (requestId !== favoritesRequestId) return;
    const grid = $("favoritesGrid");
    grid.innerHTML = "";

    $("favoritesCountBadge").textContent = items.length;
    $("favoritesCountBadge").classList.toggle("hidden", items.length === 0);

    if (items.length === 0) {
      $("favoritesEmpty").classList.remove("hidden");
      $("favoritesHeader").classList.add("hidden");
      return;
    }

    $("favoritesEmpty").classList.add("hidden");
    $("favoritesHeader").classList.remove("hidden");
    $("favoritesCount").textContent = `${items.length} ürün`;
    items.forEach((p) => grid.appendChild(renderProductCard(p)));
  } catch (err) {
    console.error("Favoriler yüklenemedi:", err);
  }
}

// The heart button on each card (shared with the rest of the site, see productGrid.js)
// unfavorites in place — reload this tab's list whenever that happens while it's visible.
onFavoritesChange(() => {
  if (favoritesLoaded && !$("favoritesPanel").classList.contains("hidden")) loadFavorites();
});

/* ── Purchase History ── */
let purchaseHistoryLoaded = false;
let purchaseHistoryRequestId = 0;

export async function loadPurchaseHistory() {
  const requestId = ++purchaseHistoryRequestId;
  try {
    const res = await apiFetch("/purchase-history");
    if (requestId !== purchaseHistoryRequestId) return;
    const items = res.items || [];
    const totalMoneySaved = res.totalMoneySaved || 0;

    const grid = $("purchaseHistoryGrid");
    if (!grid) return;
    grid.innerHTML = "";

    if ($("purchaseHistoryCountBadge")) {
      $("purchaseHistoryCountBadge").textContent = items.length;
      $("purchaseHistoryCountBadge").classList.toggle("hidden", items.length === 0);
    }

    if (items.length === 0) {
      if ($("purchaseHistoryEmpty")) $("purchaseHistoryEmpty").classList.remove("hidden");
      if ($("purchaseHistoryHeader")) $("purchaseHistoryHeader").classList.add("hidden");
      return;
    }

    if ($("purchaseHistoryEmpty")) $("purchaseHistoryEmpty").classList.add("hidden");
    if ($("purchaseHistoryHeader")) $("purchaseHistoryHeader").classList.remove("hidden");
    if ($("purchaseHistoryCount")) $("purchaseHistoryCount").textContent = `${items.length} ürün`;

    const savedEl = $("purchaseHistoryTotalSaved");
    if (savedEl) {
      if (totalMoneySaved > 0) {
        savedEl.textContent = `Toplam Tasarruf: ${formatPrice(totalMoneySaved)}`;
        savedEl.classList.remove("hidden");
      } else {
        savedEl.classList.add("hidden");
      }
    }

    items.forEach((p) => {
      const card = renderProductCard(p, {
        onRemovePurchase: async (item, cardEl) => {
          if (!confirm(`"${item.CanonicalTitle}" ürününü satın alma geçmişinizden kaldırmak istiyor musunuz?`)) {
            return;
          }
          try {
            const targetId = item.PurchaseId || item.Id;
            const res = await apiFetch(`/purchase-history/${targetId}`, { method: "DELETE" });
            cardEl.remove();

            const remainingCards = grid.querySelectorAll(".product-card").length;
            if ($("purchaseHistoryCountBadge")) {
              $("purchaseHistoryCountBadge").textContent = remainingCards;
              $("purchaseHistoryCountBadge").classList.toggle("hidden", remainingCards === 0);
            }
            if ($("purchaseHistoryCount")) {
              $("purchaseHistoryCount").textContent = `${remainingCards} ürün`;
            }

            if (savedEl) {
              const newTotalSaved = res.totalMoneySaved || 0;
              if (newTotalSaved > 0) {
                savedEl.textContent = `Toplam Tasarruf: ${formatPrice(newTotalSaved)}`;
                savedEl.classList.remove("hidden");
              } else {
                savedEl.classList.add("hidden");
              }
            }

            if (remainingCards === 0) {
              if ($("purchaseHistoryEmpty")) $("purchaseHistoryEmpty").classList.remove("hidden");
              if ($("purchaseHistoryHeader")) $("purchaseHistoryHeader").classList.add("hidden");
            }
          } catch (err) {
            console.error("Satın alma geçmişinden kaldırılamadı:", err);
            alert("Satın alma geçmişinden kaldırılırken bir hata oluştu.");
          }
        }
      });
      grid.appendChild(card);
    });
  } catch (err) {
    console.error("Satın alma geçmişi yüklenemedi:", err);
  }
}

/* ── Profile ── */
const PROFILE_FIELD_IDS = [
  "profileName",
  "profileSurname",
  "profileEmail",
  "profilePhone",
  "profileBirthdate",
  "profileCity",
  "profileOldPassword",
  "profileNewPassword",
  "profileConfirmPassword",
  "changePasswordBtn"
];
let profileDraft = null;

$("profileBirthdate").max = new Date().toISOString().slice(0, 10);
populateCitySelect("profileCity");

function populateProfileForm(user) {
  if (!user) return;
  profileDraft = user;
  if ($("profileName")) $("profileName").value = user.Name || "";
  if ($("profileSurname")) $("profileSurname").value = user.Surname || "";
  if ($("profileEmail")) $("profileEmail").value = user.Email || "";
  if ($("profilePhone")) $("profilePhone").value = user.Phone || "";
  if ($("profileBirthdate")) $("profileBirthdate").value = user.BirthDate ? user.BirthDate.slice(0, 10) : "";
  if ($("profileCity")) $("profileCity").value = user.City || "";

  const displayName = user.DisplayName || [user.Name, user.Surname].filter(Boolean).join(" ") || user.Email || "";
  const initialName = (user.Name || user.DisplayName || user.Email || "?").trim();
  const initialSurname = (user.Surname || "").trim();

  if ($("profileAvatar")) {
    $("profileAvatar").textContent = `${initialName.charAt(0)}${initialSurname.charAt(0) || ""}`.toUpperCase();
  }
  if ($("profileAvatarName")) {
    $("profileAvatarName").textContent = displayName;
  }
}

let isEditing = false;

function setProfileEditing(editing) {
  isEditing = editing;
  PROFILE_FIELD_IDS.forEach((id) => {
    const el = $(id);
    if (el) el.disabled = !editing;
  });
  $("profileActions").classList.toggle("hidden", !editing);
  $("profileEditBtn").classList.toggle("hidden", editing);

  if (!editing) {
    if ($("profileOldPassword")) $("profileOldPassword").value = "";
    if ($("profileNewPassword")) $("profileNewPassword").value = "";
    if ($("profileConfirmPassword")) $("profileConfirmPassword").value = "";
  }
}

$("profileEditBtn").addEventListener("click", () => {
  $("profileSavedMsg").classList.add("hidden");
  setProfileEditing(true);
});

$("profileCancelBtn").addEventListener("click", () => {
  populateProfileForm(profileDraft);
  setProfileEditing(false);
});

$("profilePhone").addEventListener("input", (e) => {
  e.target.value = e.target.value.replace(/\D/g, "").slice(0, 11);
});

$("profileForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("profileErrorMsg").classList.add("hidden");
  $("profileSavedMsg").classList.add("hidden");

  const email = $("profileEmail").value.trim();
  const phone = $("profilePhone").value.trim();
  const birthdateValue = $("profileBirthdate").value;

  if (!isValidEmail(email)) {
    $("profileErrorMsg").textContent = "Geçerli bir e-posta adresi giriniz (örnek: ad@alanadi.com).";
    $("profileErrorMsg").classList.remove("hidden");
    return;
  }
  if (phone && !isValidPhone(phone)) {
    $("profileErrorMsg").textContent = "Geçerli bir telefon numarası giriniz (örnek: 05xxxxxxxxx).";
    $("profileErrorMsg").classList.remove("hidden");
    return;
  }
  if (birthdateValue && birthdateValue > $("profileBirthdate").max) {
    $("profileErrorMsg").textContent = "Doğum tarihi bugünden sonraki bir tarih olamaz.";
    $("profileErrorMsg").classList.remove("hidden");
    return;
  }

  $("profileSaveBtn").disabled = true;

  try {
    const updatedUser = await apiFetch("/auth/me", {
      method: "PUT",
      body: JSON.stringify({
        Email: $("profileEmail").value.trim(),
        Name: $("profileName").value.trim(),
        Surname: $("profileSurname").value.trim(),
        Phone: $("profilePhone").value.trim(),
        BirthDate: $("profileBirthdate").value || null,
        City: $("profileCity").value || null,
      }),
    });

    const session = getSession();
    setSession(session.token, updatedUser, session.remember);
    setProfileEditing(false);
    $("profileSavedMsg").classList.remove("hidden");
  } catch (err) {
    $("profileErrorMsg").textContent = err.message;
    $("profileErrorMsg").classList.remove("hidden");
  } finally {
    $("profileSaveBtn").disabled = false;
  }
});

/* ── Password Form Submit ── */
const passwordForm = $("passwordForm");
if (passwordForm) {
  passwordForm.addEventListener("submit", async (e) => {
    e.preventDefault();
    const errorMsg = $("passwordErrorMsg");
    const savedMsg = $("passwordSavedMsg");
    const saveBtn = $("changePasswordBtn");

    errorMsg.classList.add("hidden");
    savedMsg.classList.add("hidden");

    const oldPassword = $("profileOldPassword").value;
    const newPassword = $("profileNewPassword").value;
    const confirmPassword = $("profileConfirmPassword").value;

    // Pre-checks in exact order:
    // 2- New password strong enough
    if (!newPassword || newPassword.length < 6) {
      errorMsg.textContent = "Yeni şifre en az 6 karakter olmalıdır.";
      errorMsg.classList.remove("hidden");
      return;
    }

    // 3- New password same as old one
    if (newPassword === oldPassword) {
      errorMsg.textContent = "Yeni şifreniz mevcut şifrenizle aynı olamaz.";
      errorMsg.classList.remove("hidden");
      return;
    }

    // 4- Confirm password field matches
    if (newPassword !== confirmPassword) {
      errorMsg.textContent = "Yeni şifreler eşleşmiyor.";
      errorMsg.classList.remove("hidden");
      return;
    }

    saveBtn.disabled = true;

    try {
      // 1- Check if old password matches & update DB query via backend
      const res = await apiFetch("/auth/change-password", {
        method: "POST",
        body: JSON.stringify({
          OldPassword: oldPassword,
          NewPassword: newPassword,
          ConfirmPassword: confirmPassword,
        }),
      });

      savedMsg.textContent = res.message || "Şifreniz başarıyla güncellendi.";
      savedMsg.classList.remove("hidden");

      $("profileOldPassword").value = "";
      $("profileNewPassword").value = "";
      $("profileConfirmPassword").value = "";
    } catch (err) {
      errorMsg.textContent = err.message || "Şifre değiştirilirken bir hata oluştu.";
      errorMsg.classList.remove("hidden");
    } finally {
      saveBtn.disabled = false;
    }
  });
}

/* ── Guest / logged-in state ── */
// renderAccountState is itself an onAuthChange listener (see bottom of file). The /auth/me
// refresh below calls setSession() on success, which re-fires every onAuthChange listener —
// including this one — so without a one-shot guard it refetches, calls setSession again, and
// loops forever. That flood of in-flight /auth/me calls is also what broke logout: a request
// started before clearSession() could resolve just after it and write the old token straight
// back into storage, so the redirect to index.html landed still "logged in". Fetching fresh
// profile details only once per page load (same pattern as favoritesLoaded below) breaks the
// cycle.
let profileRefreshed = false;

async function renderAccountState() {
  const session = getSession();
  $("accountGuest").classList.toggle("hidden", !!session);
  $("accountLoggedIn").classList.toggle("hidden", !session);
  if (!session) {
    // Reset the one-shot guards on logout so signing back in as a different account (without
    // a full page reload — e.g. via the login modal right here) still refreshes their profile
    // and favorites instead of skipping both because a previous session already used them up.
    profileRefreshed = false;
    favoritesLoaded = false;
    purchaseHistoryLoaded = false;
    return;
  }

  $("accountName").textContent = session.user.DisplayName || session.user.Name || session.user.Email.split("@")[0];
  $("accountEmail").textContent = session.user.Email;
  if (!isEditing) {
    populateProfileForm(session.user);
    setProfileEditing(false);
  }

  if (!profileRefreshed) {
    profileRefreshed = true;
    try {
      const freshUser = await apiFetch("/auth/me");
      if (freshUser) {
        setSession(session.token, freshUser, session.remember);
        $("accountName").textContent = freshUser.DisplayName || freshUser.Name || freshUser.Email.split("@")[0];
        $("accountEmail").textContent = freshUser.Email;
        if (!isEditing) {
          populateProfileForm(freshUser);
        }
      }
    } catch (err) {
      // Keep cached session user if offline
    }
  }

  if (!favoritesLoaded) {
    favoritesLoaded = true;
    loadFavorites();
  }
}

$("accountGuestLoginBtn").addEventListener("click", () => openLoginModal());
$("accountLogoutBtn").addEventListener("click", () => openLogoutModal());

onAuthChange(renderAccountState);
renderAccountState();
