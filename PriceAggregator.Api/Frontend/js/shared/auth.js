import { API_BASE } from "./api.js";

const STORAGE_KEY = "tutumlu_auth";

// "Beni hatırla" decides which storage backs the session: localStorage survives browser
// restarts, sessionStorage clears when the tab closes. Both are checked on read since we
// don't know which one holds it until we look.
function readSession() {
  const raw = localStorage.getItem(STORAGE_KEY) ?? sessionStorage.getItem(STORAGE_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function clearStorages() {
  localStorage.removeItem(STORAGE_KEY);
  sessionStorage.removeItem(STORAGE_KEY);
}

let session = readSession();
const listeners = new Set();

export function getSession() {
  return session;
}

export function getToken() {
  return session?.token ?? null;
}

export function onAuthChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function setSession(token, user, remember) {
  session = { token, user, remember };
  clearStorages();
  (remember ? localStorage : sessionStorage).setItem(STORAGE_KEY, JSON.stringify(session));
  listeners.forEach((fn) => fn(session));
}

export function clearSession() {
  session = null;
  clearStorages();
  listeners.forEach((fn) => fn(session));
}

// Called once at page load: confirms the stored token still validates server-side (it may
// have expired, or the DB row it points at may be gone) and refreshes the cached user fields.
// A network failure is not treated as "logged out" — only a real 401 clears the session.
export async function verifySession() {
  if (!session) return;
  try {
    const res = await fetch(`${API_BASE}/auth/me`, {
      headers: { Authorization: `Bearer ${session.token}` },
    });
    if (!res.ok) {
      if (res.status === 401) clearSession();
      return;
    }
    const user = await res.json();
    setSession(session.token, user, session.remember);
  } catch {
    // offline/unreachable — keep the existing session as-is
  }
}

async function authFetch(path, body) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  let data = null;
  try {
    data = await res.json();
  } catch {
    // 401 responses have no body
  }

  if (!res.ok) {
    if (res.status === 401) throw new Error("E-posta veya şifre hatalı.");
    throw new Error(data?.error ?? "Bir şeyler ters gitti, lütfen tekrar deneyin.");
  }

  return data;
}

export async function login(email, password, remember) {
  const data = await authFetch("/auth/login", { Email: email, Password: password });
  setSession(data.token, data.user, remember);
  return data.user;
}

export async function register(email, password, name, surname, phone, birthdate, city, remember) {
  const data = await authFetch("/auth/register", {
    Email: email, Password: password, Name: name, Surname: surname, Phone: phone, BirthDate: birthdate, City: city,
  });
  setSession(data.token, data.user, remember);
  return data.user;
}

// Authenticated fetch for endpoints beyond /auth/* — attaches the bearer token and
// normalizes error handling the same way authFetch does for login/register.
export async function apiFetch(path, options = {}) {
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(getToken() ? { Authorization: `Bearer ${getToken()}` } : {}),
      ...(options.headers || {}),
    },
  });

  let data = null;
  try {
    data = await res.json();
  } catch {
    // No body (e.g. 204/empty 200 responses)
  }

  if (!res.ok) {
    throw new Error(data?.error ?? "Bir şeyler ters gitti, lütfen tekrar deneyin.");
  }

  return data;
}
