// Floating "compare" button docked to the right edge of the viewport. Built entirely in JS
// and appended to <body> rather than duplicated as static markup in every HTML page (this
// project has no shared-partial mechanism — see products.html/index.html/account.html,
// which already copy-paste their header/modals verbatim).
import { getCompareItems, onCompareChange, onCompareLimitReached, MAX_COMPARE_ITEMS } from "../shared/compare.js";

const COMPARE_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
  <path d="M8 3v14m0 0-4-4m4 4 4-4" />
  <path d="M16 21V7m0 0-4 4m4-4 4 4" />
</svg>`;

let wrapEl = null;
let warningTimeout = null;

function buildButton() {
  const wrap = document.createElement("div");
  wrap.className = "compare-fab-wrap hidden";
  wrap.innerHTML = `
    <p class="compare-fab-warning hidden" id="compareFabWarning">En fazla ${MAX_COMPARE_ITEMS} ürün karşılaştırabilirsiniz.</p>
    <button type="button" class="compare-fab" id="compareFabBtn" aria-label="Ürünleri karşılaştır">
      ${COMPARE_ICON}
      <span class="compare-fab-badge" id="compareFabBadge">0</span>
    </button>`;
  document.body.appendChild(wrap);

  wrap.querySelector("#compareFabBtn").addEventListener("click", () => {
    const ids = getCompareItems().map((p) => p.id);
    if (ids.length === 0) return;
    window.location.href = `compare.html?ids=${ids.join(",")}`;
  });

  return wrap;
}

function render() {
  if (!wrapEl) return;
  const count = getCompareItems().length;
  wrapEl.querySelector("#compareFabBadge").textContent = count;
  wrapEl.classList.toggle("hidden", count === 0);
}

function flashWarning() {
  if (!wrapEl) return;
  const warning = wrapEl.querySelector("#compareFabWarning");
  warning.classList.remove("hidden");
  clearTimeout(warningTimeout);
  warningTimeout = setTimeout(() => warning.classList.add("hidden"), 2500);
}

// Idempotent — safe to call once per page even though nothing currently calls it twice.
export function initCompareBar() {
  if (wrapEl) return;
  wrapEl = buildButton();
  render();
  onCompareChange(render);
  onCompareLimitReached(flashWarning);
}
