import { $, formatTimeAgo } from "./utils.js";
import { getSession, onAuthChange, apiFetch } from "./auth.js";

let notifications = [];
let unreadCount = 0;
const listeners = new Set();

export function onNotificationsChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

function notify() {
  listeners.forEach((fn) => fn({ notifications, unreadCount }));
}

export function updateNotificationBellUI(count) {
  const btn = $("notificationBtn");
  const badge = $("notificationBadge");
  if (!btn || !badge) return;

  if (!getSession()) {
    btn.hidden = true;
    badge.hidden = true;
    return;
  }

  btn.hidden = false;
  unreadCount = count;

  if (count > 0) {
    btn.classList.add("has-unread");
    badge.hidden = false;
    badge.textContent = count > 99 ? "99+" : count;
  } else {
    btn.classList.remove("has-unread");
    badge.hidden = true;
    badge.textContent = "0";
  }
}

export async function refreshNotifications() {
  if (!getSession()) {
    notifications = [];
    unreadCount = 0;
    updateNotificationBellUI(0);
    notify();
    return;
  }

  try {
    const list = await apiFetch("/notifications");
    notifications = Array.isArray(list) ? list : [];
    unreadCount = notifications.filter((n) => !n.ReadStatus).length;
    updateNotificationBellUI(unreadCount);
    notify();
  } catch (err) {
    console.error("Bildirimler yüklenemedi:", err);
  }
}

export async function markAsRead(notificationId) {
  if (!getSession()) return;
  try {
    await apiFetch(`/notifications/${notificationId}/read`, { method: "POST" });
    const item = notifications.find((n) => n.NotificationId === notificationId);
    if (item && !item.ReadStatus) {
      item.ReadStatus = true;
      unreadCount = Math.max(0, unreadCount - 1);
      updateNotificationBellUI(unreadCount);
      notify();
    }
  } catch (err) {
    console.error("Bildirim okundu işaretlenemedi:", err);
  }
}

export async function markAllAsRead() {
  if (!getSession()) return;
  try {
    await apiFetch("/notifications/read-all", { method: "POST" });
    notifications.forEach((n) => (n.ReadStatus = true));
    unreadCount = 0;
    updateNotificationBellUI(0);
    notify();
  } catch (err) {
    console.error("Tüm bildirimler okundu işaretlenemedi:", err);
  }
}

export async function deleteNotification(notificationId) {
  if (!getSession()) return;
  try {
    await apiFetch(`/notifications/${notificationId}`, { method: "DELETE" });
    const item = notifications.find((n) => n.NotificationId === notificationId);
    notifications = notifications.filter((n) => n.NotificationId !== notificationId);
    if (item && !item.ReadStatus) {
      unreadCount = Math.max(0, unreadCount - 1);
      updateNotificationBellUI(unreadCount);
    }
    notify();
  } catch (err) {
    console.error("Bildirim silinemedi:", err);
  }
}

export function getCachedNotifications() {
  return notifications;
}

export function getCachedUnreadCount() {
  return unreadCount;
}

onAuthChange(() => {
  refreshNotifications();
});
