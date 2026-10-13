import { $, isValidEmail, isValidPhone, populateCitySelect } from "../shared/utils.js";
import { getSession, onAuthChange, clearSession, login, register, verifySession } from "../shared/auth.js";

let mode = "login"; // "login" | "register"

// Register-only fields (mirrors every field on the account page's Profil Bilgilerim tab,
// plus a password-confirm field) — hidden and non-required in login mode, shown and
// required in register mode.
const REGISTER_ONLY_FIELDS = [
  ["loginNameField", "loginName"],
  ["loginSurnameField", "loginSurname"],
  ["loginPhoneField", "loginPhone"],
  ["loginBirthdateField", "loginBirthdate"],
  ["loginCityField", "loginCity"],
  ["loginPasswordConfirmField", "loginPasswordConfirm"],
];

function applyMode() {
  const isRegister = mode === "register";
  $("loginModalEyebrow").textContent = isRegister ? "HESAP OLUŞTUR" : "HOŞ GELDİN";
  $("loginModalTitle").textContent = isRegister ? "Hesap oluştur" : "Giriş yap";
  $("loginModalDesc").textContent = isRegister
    ? "Ücretsiz hesap oluştur, fiyat düştüğünde ilk sen haberdar ol."
    : "Hesabına giriş yaparak takip ettiğin fiyatlara ulaş.";
  $("loginSubmitBtn").textContent = isRegister ? "Kayıt ol" : "Giriş yap";
  $("loginSignupPrompt").textContent = isRegister ? "Zaten hesabın var mı?" : "Henüz hesabın yok mu?";
  $("loginSignupToggle").textContent = isRegister ? "Giriş yap" : "Hemen kayıt ol";

  REGISTER_ONLY_FIELDS.forEach(([fieldId, inputId]) => {
    $(fieldId).classList.toggle("hidden", !isRegister);
    $(inputId).required = isRegister;
  });
}

function toggleMode() {
  mode = mode === "login" ? "register" : "login";
  $("loginFormMessage").classList.add("hidden");
  applyMode();
}

function showFormError(text) {
  const message = $("loginFormMessage");
  message.textContent = text;
  message.classList.remove("hidden");
  message.classList.add("error");
}

async function handleSubmit(event) {
  event.preventDefault();
  const message = $("loginFormMessage");
  message.classList.add("hidden");
  message.classList.remove("error");

  const email = $("loginEmail").value.trim();
  const password = $("loginPassword").value;
  const remember = $("loginRemember").checked;
  const submitBtn = $("loginSubmitBtn");

  if (!isValidEmail(email)) {
    showFormError("Geçerli bir e-posta adresi giriniz (örnek: ad@alanadi.com).");
    return;
  }

  if (mode === "register") {
    const name = $("loginName").value.trim();
    const surname = $("loginSurname").value.trim();
    const phone = $("loginPhone").value.trim();
    const birthdate = $("loginBirthdate").value;
    const city = $("loginCity").value;
    const passwordConfirm = $("loginPasswordConfirm").value;

    if (!name || !surname || !phone || !birthdate || !city) {
      showFormError("Lütfen tüm alanları doldur.");
      return;
    }
    if (!isValidPhone(phone)) {
      showFormError("Geçerli bir telefon numarası giriniz (örnek: 05xxxxxxxxx).");
      return;
    }
    if (birthdate > todayIsoDate()) {
      showFormError("Doğum tarihi bugünden sonraki bir tarih olamaz.");
      return;
    }
    if (password !== passwordConfirm) {
      showFormError("Şifreler eşleşmiyor.");
      return;
    }

    submitBtn.disabled = true;
    try {
      await register(email, password, name, surname, phone, birthdate, city, remember);
      closeLoginModal();
    } catch (err) {
      showFormError(err.message);
    } finally {
      submitBtn.disabled = false;
    }
    return;
  }

  submitBtn.disabled = true;
  try {
    await login(email, password, remember);
    closeLoginModal();
  } catch (err) {
    showFormError(err.message);
  } finally {
    submitBtn.disabled = false;
  }
}

function todayIsoDate() {
  return new Date().toISOString().slice(0, 10);
}

function togglePasswordVisibility(inputId, toggleId) {
  const input = $(inputId);
  const toggle = $(toggleId);
  const showing = input.type === "text";
  input.type = showing ? "password" : "text";
  toggle.textContent = showing ? "Göster" : "Gizle";
}

// Reflects auth state on every "Giriş Yap" button (header markup is duplicated per page) —
// logged in shows the user's name and doubles as the logout control, logged out opens the modal.
function renderAuthButtons() {
  const session = getSession();
  document.querySelectorAll(".login-btn").forEach((btn) => {
    if (session) {
      btn.textContent = session.user.DisplayName || session.user.Email.split("@")[0];
    } else {
      btn.textContent = "Giriş Yap";
    }
  });
}

// Logged out: opens the login modal. Logged in: goes to the account page — logging out
// itself now lives there rather than behind this header button.
function handleAuthButtonClick() {
  if (getSession()) {
    window.location.href = "account.html";
  } else {
    openLoginModal();
  }
}

function handleLogoutConfirm() {
  clearSession();
  closeLogoutModal();
  if (window.location.pathname.endsWith("account.html")) {
    window.location.href = "index.html";
  } else {
    window.location.reload();
  }
}

export function openLogoutModal() {
  $("logoutModal").classList.remove("hidden");
  document.body.style.overflow = "hidden";
}

export function closeLogoutModal() {
  $("logoutModal").classList.add("hidden");
  document.body.style.overflow = "";
}

export function openLoginModal() {
  mode = "login";
  applyMode();
  $("loginForm").reset();
  $("loginFormMessage").classList.add("hidden");
  $("loginModal").classList.remove("hidden");
  document.body.style.overflow = "hidden";
}

export function closeLoginModal() {
  $("loginModal").classList.add("hidden");
  document.body.style.overflow = "";
}

export function initLoginModal() {
  populateCitySelect("loginCity");
  $("loginBirthdate").max = todayIsoDate();
  $("loginPhone").addEventListener("input", (e) => {
    e.target.value = e.target.value.replace(/\D/g, "").slice(0, 11);
  });
  document.querySelectorAll(".login-btn").forEach((btn) => btn.addEventListener("click", handleAuthButtonClick));
  $("closeLoginModal").addEventListener("click", closeLoginModal);
  $("loginModal").querySelector(".login-modal-backdrop").addEventListener("click", closeLoginModal);
  $("toggleLoginPassword").addEventListener("click", () => togglePasswordVisibility("loginPassword", "toggleLoginPassword"));
  $("toggleLoginPasswordConfirm").addEventListener("click", () => togglePasswordVisibility("loginPasswordConfirm", "toggleLoginPasswordConfirm"));
  $("loginSignupToggle").addEventListener("click", toggleMode);
  $("loginForm").addEventListener("submit", handleSubmit);

  $("logoutCancelBtn").addEventListener("click", closeLogoutModal);
  $("logoutConfirmBtn").addEventListener("click", handleLogoutConfirm);
  $("logoutModal").querySelector(".logout-modal-backdrop").addEventListener("click", closeLogoutModal);

  renderAuthButtons();
  onAuthChange(renderAuthButtons);
  verifySession();
}
