import { $, escapeHtml, formatTimeAgo } from "../shared/utils.js";
import { openProductDetail } from "./modal.js";
import {
  refreshNotifications,
  markAsRead,
  markAllAsRead,
  deleteNotification,
  getCachedNotifications,
  onNotificationsChange
} from "../shared/notifications.js";

const TRASH_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
  <path d="M3 6h18" />
  <path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2" />
  <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
  <path d="M10 11v6M14 11v6" />
</svg>`;

export function openNotificationModal() {
  const modal = $("notificationModal");
  if (!modal) return;
  modal.classList.remove("hidden");
  document.body.style.overflow = "hidden";
  refreshNotifications();
}

export function closeNotificationModal() {
  const modal = $("notificationModal");
  if (!modal) return;
  modal.classList.add("hidden");
  document.body.style.overflow = "";
}

function renderNotificationList() {
  const container = $("notificationList");
  if (!container) return;

  const notifications = getCachedNotifications();

  if (!notifications || notifications.length === 0) {
    container.innerHTML = `
      <div class="state-message">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
          <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
          <path d="M13.73 21a2 2 0 0 1-3.46 0" />
        </svg>
        <h3>Bildirim bulunmuyor</h3>
        <p>Favorilerinize eklediğiniz ürünlerde indirim olduğunda burada haber verilecektir.</p>
      </div>`;
    return;
  }

  container.innerHTML = "";

  notifications.forEach((item) => {
    const card = document.createElement("div");
    card.className = "notification-item" + (item.ReadStatus ? "" : " unread");

    const imgHtml = item.ImageUrl
      ? `<img src="${escapeHtml(item.ImageUrl)}" alt="${escapeHtml(item.CanonicalTitle)}" class="notification-item-img">`
      : `<div class="notification-item-img" style="display:flex;align-items:center;justify-content:center;color:#94a3b8;">📦</div>`;

    const discountTag = item.DiscountPercent
      ? `<span class="notification-discount-tag">%${item.DiscountPercent} İndirim!</span>`
      : "";

    card.innerHTML = `
      ${imgHtml}
      <div class="notification-item-body">
        <p class="notification-item-msg">${escapeHtml(item.NotificationMessage)}</p>
        <div class="notification-item-meta">
          <span class="notification-time">${formatTimeAgo(item.NotificationDate)}</span>
          ${discountTag}
        </div>
      </div>
      ${item.ReadStatus ? "" : '<span class="notification-unread-dot"></span>'}
      <button type="button" class="notification-delete-btn" aria-label="Bildirimi sil">${TRASH_ICON}</button>`;

    card.addEventListener("click", async () => {
      if (!item.ReadStatus) {
        await markAsRead(item.NotificationId);
      }
      closeNotificationModal();
      openProductDetail(item.OnSaleProductId, item.CanonicalTitle);
    });

    const deleteBtn = card.querySelector(".notification-delete-btn");
    deleteBtn.addEventListener("click", async (e) => {
      e.stopPropagation();
      deleteBtn.disabled = true;
      await deleteNotification(item.NotificationId);
    });

    container.appendChild(card);
  });
}

export function initNotificationModal() {
  const btn = $("notificationBtn");
  const modal = $("notificationModal");
  const backdrop = modal?.querySelector(".notification-modal-backdrop");
  const closeBtn = $("closeNotificationModal");
  const markAllBtn = $("markAllNotificationsBtn");

  if (btn) {
    btn.addEventListener("click", () => {
      if (modal?.classList.contains("hidden")) openNotificationModal();
      else closeNotificationModal();
    });
  }

  if (backdrop) backdrop.addEventListener("click", closeNotificationModal);
  if (closeBtn) closeBtn.addEventListener("click", closeNotificationModal);

  if (markAllBtn) {
    markAllBtn.addEventListener("click", async () => {
      await markAllAsRead();
    });
  }

  onNotificationsChange(() => {
    if (modal && !modal.classList.contains("hidden")) {
      renderNotificationList();
    }
  });

  // Initial fetch if logged in
  refreshNotifications();
}
