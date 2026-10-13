import { $, escapeHtml } from "./utils.js";
import { getSession, apiFetch } from "./auth.js";

let containerEl = null;

function getPendingQueue() {
  const raw = sessionStorage.getItem("pendingPurchaseRedirects");
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    sessionStorage.removeItem("pendingPurchaseRedirects");
    return [];
  }
}

function savePendingQueue(queue) {
  if (!queue || queue.length === 0) {
    sessionStorage.removeItem("pendingPurchaseRedirects");
  } else {
    sessionStorage.setItem("pendingPurchaseRedirects", JSON.stringify(queue));
  }
}

function removeCandidateFromQueue(productId) {
  const queue = getPendingQueue().filter((p) => p.productId !== productId);
  savePendingQueue(queue);
}

function ensureContainerExists() {
  if (containerEl && document.body.contains(containerEl)) return containerEl;

  containerEl = document.createElement("div");
  containerEl.id = "purchaseToastContainer";
  containerEl.style.cssText = `
    position: fixed;
    bottom: 24px;
    right: 24px;
    z-index: 9999;
    display: flex;
    flex-direction: column-reverse;
    gap: 12px;
    max-width: 380px;
    width: calc(100vw - 48px);
    pointer-events: none;
  `;

  if (!document.getElementById("purchaseToastStyle")) {
    const style = document.createElement("style");
    style.id = "purchaseToastStyle";
    style.textContent = `
      @keyframes purchaseToastSlideIn {
        from { transform: translateY(20px) scale(0.95); opacity: 0; }
        to { transform: translateY(0) scale(1); opacity: 1; }
      }
      .purchase-confirm-toast-card {
        pointer-events: auto;
        background: #ffffff;
        border: 1px solid #e2e8f0;
        border-radius: 12px;
        box-shadow: 0 10px 25px -5px rgba(0,0,0,0.1), 0 8px 10px -6px rgba(0,0,0,0.1);
        padding: 16px;
        display: flex;
        flex-direction: column;
        gap: 12px;
        font-family: 'Plus Jakarta Sans', sans-serif;
        animation: purchaseToastSlideIn 0.3s cubic-bezier(0.16, 1, 0.3, 1);
        transition: all 0.25s ease;
      }
    `;
    document.head.appendChild(style);
  }

  document.body.appendChild(containerEl);
  return containerEl;
}

export function checkPendingPurchaseConfirmation() {
  if (!getSession()) {
    if (containerEl) {
      containerEl.innerHTML = "";
    }
    return;
  }

  const queue = getPendingQueue();
  if (queue.length === 0) {
    if (containerEl) {
      containerEl.innerHTML = "";
    }
    return;
  }

  const container = ensureContainerExists();
  const existingCardIds = new Set(
    Array.from(container.querySelectorAll("[data-toast-product-id]")).map((el) => Number(el.dataset.toastProductId))
  );

  const currentProductIds = new Set(queue.map((item) => item.productId));

  // Remove cards that are no longer in the queue
  container.querySelectorAll("[data-toast-product-id]").forEach((card) => {
    const id = Number(card.dataset.toastProductId);
    if (!currentProductIds.has(id)) {
      card.remove();
    }
  });

  // Render cards for all candidates in the queue simultaneously
  queue.forEach((candidate) => {
    if (!candidate || !candidate.productId || !candidate.sellerId) return;
    if (existingCardIds.has(candidate.productId)) return; // Already rendered

    const card = renderToastCard(candidate);
    container.appendChild(card);
  });
}

function renderToastCard(candidate) {
  const card = document.createElement("div");
  card.className = "purchase-confirm-toast-card";
  card.setAttribute("role", "alert");
  card.dataset.toastProductId = candidate.productId;

  const imgHtml = candidate.imageUrl
    ? `<img src="${escapeHtml(candidate.imageUrl)}" alt="${escapeHtml(candidate.productTitle)}" style="width:44px;height:44px;object-fit:contain;border-radius:8px;border:1px solid #f1f5f9;background:#f8fafc;flex-shrink:0;">`
    : `<div style="width:44px;height:44px;border-radius:8px;background:#eff6ff;color:#3b82f6;display:flex;align-items:center;justify-content:center;font-size:18px;flex-shrink:0;">🛍️</div>`;

  card.innerHTML = `
    <div style="display:flex;align-items:flex-start;gap:12px;">
      ${imgHtml}
      <div style="flex:1;min-width:0;">
        <p style="margin:0 0 2px 0;font-size:11px;font-weight:700;color:#3b82f6;letter-spacing:0.04em;text-transform:uppercase;">Satın Alma Doğrulama</p>
        <p style="margin:0 0 4px 0;font-size:13px;font-weight:600;color:#0f172a;line-height:1.3;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;">${escapeHtml(candidate.productTitle)}</p>
        <p style="margin:0;font-size:12px;color:#64748b;"><strong>${escapeHtml(candidate.storeName)}</strong> mağazasından bu ürünü satın aldınız mı?</p>
      </div>
    </div>
    <div style="display:flex;align-items:center;justify-content:flex-end;gap:8px;margin-top:4px;">
      <button type="button" class="toast-btn-no" style="padding:6px 12px;border:1px solid #cbd5e1;background:#f8fafc;color:#475569;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;transition:all 0.2s;">Hayır</button>
      <button type="button" class="toast-btn-yes" style="padding:6px 14px;border:none;background:#10b981;color:#ffffff;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;transition:all 0.2s;box-shadow:0 2px 4px rgba(16,185,129,0.2);">Evet, Satın Aldım</button>
    </div>
  `;

  const btnYes = card.querySelector(".toast-btn-yes");
  const btnNo = card.querySelector(".toast-btn-no");

  btnYes.addEventListener("click", async () => {
    btnYes.disabled = true;
    btnNo.disabled = true;
    btnYes.textContent = "Kaydediliyor...";
    try {
      await apiFetch("/purchase-history/confirm", {
        method: "POST",
        body: JSON.stringify({
          ProductId: candidate.productId,
          SellerId: candidate.sellerId,
          ActualPrice: candidate.actualPrice || candidate.discountedPrice || 0,
          DiscountedPrice: candidate.discountedPrice || 0
        })
      });
      removeCandidateFromQueue(candidate.productId);
      card.remove();

      // If user is currently on account page, reload Purchase History grid & savings
      const purchaseGrid = $("purchaseHistoryGrid");
      if (purchaseGrid) {
        import("../pages/account.js").then((m) => {
          if (m.loadPurchaseHistory) m.loadPurchaseHistory();
        }).catch(() => {});
      }
    } catch (err) {
      console.error("Satın alma onaylanamadı:", err);
      removeCandidateFromQueue(candidate.productId);
      card.remove();
    }
  });

  btnNo.addEventListener("click", () => {
    removeCandidateFromQueue(candidate.productId);
    card.remove();
  });

  return card;
}

let isInitialized = false;

export function initPurchaseNotifier() {
  if (isInitialized) {
    checkPendingPurchaseConfirmation();
    return;
  }
  isInitialized = true;

  // Listen to popstate (back/forward browser navigation)
  window.addEventListener("popstate", () => {
    checkPendingPurchaseConfirmation();
  });

  // Intercept pushState and replaceState for SPA / in-app URL changes
  const origPushState = history.pushState;
  history.pushState = function (...args) {
    origPushState.apply(this, args);
    checkPendingPurchaseConfirmation();
  };

  const origReplaceState = history.replaceState;
  history.replaceState = function (...args) {
    origReplaceState.apply(this, args);
    checkPendingPurchaseConfirmation();
  };

  // Initial check on page load
  checkPendingPurchaseConfirmation();
}
