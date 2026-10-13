import { $, escapeHtml, formatPrice, formatUnitPrice } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { getToken } from "../shared/auth.js";

let priceChartInstance = null;

// Fire-and-forget: feeds /api/products/recommended's view signal (see UserProductViews).
// Guests have nothing to attach a view to (recommendations are only ever personalized for
// logged-in users), so this is skipped rather than sent unauthenticated. Never blocks or
// fails the modal open — a lost view row just means one weaker recommendation signal, not a
// broken product page.
function recordProductView(productId) {
  const token = getToken();
  if (!token) return;
  fetch(`${API_BASE}/products/${productId}/view`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` }
  }).catch((err) => console.error("Ürün görüntüleme kaydedilemedi:", err));
}

export async function openProductDetail(masterId, title) {
  const modal = $("modal");
  const offersContainer = $("offers");
  const similarContainer = $("similarProducts");
  const infoContainer = $("productInfo");
  $("modalTitle").textContent = title;
  offersContainer.innerHTML = `<div class="state-message"><p>Yükleniyor...</p></div>`;
  similarContainer.innerHTML = `<div class="state-message"><p>Yükleniyor...</p></div>`;
  if (infoContainer) infoContainer.innerHTML = `<div class="state-message"><p>Yükleniyor...</p></div>`;
  modal.classList.remove("hidden");
  document.body.style.overflow = "hidden";
  recordProductView(masterId);

  let offers = [];
  try {
    const res = await fetch(`${API_BASE}/products/${masterId}`);
    const data = await res.json();
    offers = data.Offers || [];
    renderOffers(offersContainer, offers, data.Product, infoContainer);
  } catch (err) {
    offersContainer.innerHTML = `<div class="state-message"><p>Fiyatlar yüklenemedi.</p></div>`;
    if (infoContainer) infoContainer.innerHTML = `<div class="state-message"><p>Ürün bilgileri yüklenemedi.</p></div>`;
    console.error(err);
  }

  // Fetch price history with offers guaranteed to be loaded
  fetch(`${API_BASE}/products/${masterId}/price-history`)
    .then((res) => res.json())
    .then((history) => renderPriceHistoryChart(history, offers))
    .catch((err) => {
      console.error("Price history fetch error:", err);
      renderPriceHistoryChart([], offers);
    });

  // Fetch similar products in parallel
  fetch(`${API_BASE}/products/${masterId}/similar`)
    .then((res) => res.json())
    .then((products) => renderSimilarProducts(similarContainer, products))
    .catch((err) => {
      similarContainer.innerHTML = `<div class="state-message"><p>Benzer ürünler yüklenemedi.</p></div>`;
      console.error(err);
    });
}

function renderOffers(offersContainer, offers, product, infoContainer) {
  offersContainer.innerHTML = "";
  if (!offers || offers.length === 0) {
    offersContainer.innerHTML = `<div class="state-message"><p>Mağaza teklifi bulunamadı.</p></div>`;
    if (infoContainer) renderProductInfo(infoContainer, product, null);
    return;
  }
  const validPrices = offers.map((o) => o.Price).filter((p) => p != null && p > 0);
  const minPrice = validPrices.length > 0 ? Math.min(...validPrices) : null;

  offers.forEach((offer, index) => {
    const isCheapest = minPrice != null && offer.Price === minPrice;
    const row = document.createElement("div");
    row.className = "offer-row" + (isCheapest ? " cheapest" : "") + (index === 0 ? " selected" : "");
    row.setAttribute("role", "button");
    row.tabIndex = 0;
    row.innerHTML = `
      ${offer.ImageUrl ? `<img src="${escapeHtml(offer.ImageUrl)}" alt="${escapeHtml(offer.Source)}">` : ""}
      <div class="offer-store">
        <div class="offer-source">${escapeHtml(offer.Source)}</div>
        ${isCheapest ? '<span class="offer-badge">En ucuz</span>' : ""}
      </div>
      <div class="offer-price-wrap">
        ${offer.DiscountPercent ? `<span class="offer-discount-badge">%${offer.DiscountPercent} indirim</span>` : ""}
        ${offer.DiscountPercent ? `<span class="product-old-price">${formatPrice(offer.OldPrice)}</span>` : ""}
        <span class="offer-price">${formatPrice(offer.Price)}</span>
        ${formatUnitPrice(offer.UnitPrice, offer.UnitLabel)}
      </div>
      <a href="${escapeHtml(offer.ProductUrl)}" target="_blank" rel="noopener noreferrer" class="offer-link">
        Git
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>
          <polyline points="15 3 21 3 21 9"/>
          <line x1="10" y1="14" x2="21" y2="3"/>
        </svg>
      </a>`;

    const offerLink = row.querySelector(".offer-link");
    if (offerLink) {
      offerLink.addEventListener("click", () => {
        if (product && offer && offer.SellerId) {
          let pending = [];
          try {
            const raw = sessionStorage.getItem("pendingPurchaseRedirects");
            if (raw) pending = JSON.parse(raw);
            if (!Array.isArray(pending)) pending = [];
          } catch {
            pending = [];
          }

          const allOfferPrices = Array.isArray(offers) ? offers.map((o) => Number(o.Price) || 0) : [];
          const maxOfferPrice = allOfferPrices.length > 0 ? Math.max(...allOfferPrices) : (Number(offer.Price) || 0);

          const newCandidate = {
            productId: product.Id,
            sellerId: offer.SellerId,
            storeName: offer.StoreName || offer.Source,
            productTitle: product.CanonicalTitle || "",
            imageUrl: offer.ImageUrl || "",
            actualPrice: maxOfferPrice,
            discountedPrice: Number(offer.Price) || 0,
            timestamp: Date.now()
          };

          const existingIdx = pending.findIndex((p) => p.productId === product.Id);
          if (existingIdx !== -1) {
            pending[existingIdx] = newCandidate;
          } else {
            if (pending.length >= 3) {
              pending.shift();
            }
            pending.push(newCandidate);
          }

          sessionStorage.setItem("pendingPurchaseRedirects", JSON.stringify(pending));
        }
      });
    }

    const select = () => {
      offersContainer.querySelectorAll(".offer-row.selected").forEach((el) => el.classList.remove("selected"));
      row.classList.add("selected");
      if (infoContainer) renderProductInfo(infoContainer, product, offer);
    };
    row.addEventListener("click", (e) => {
      if (e.target.closest(".offer-link")) return;
      select();
    });
    row.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        select();
      }
    });

    offersContainer.appendChild(row);
  });

  if (infoContainer) renderProductInfo(infoContainer, product, offers[0]);
}

function renderProductInfo(container, product, offer) {
  if (!product) {
    container.innerHTML = `<div class="state-message"><p>Ürün bilgisi bulunamadı.</p></div>`;
    return;
  }

  const sizeParts = [];
  if (product.SizeValue) sizeParts.push(`${product.SizeValue} ${escapeHtml(product.SizeUnit || "")}`.trim());
  if (product.PackQuantity && product.PackQuantity > 1) sizeParts.push(`${product.PackQuantity} adet`);

  const facts = [];
  if (product.Brand) facts.push(["Marka", escapeHtml(product.Brand)]);
  if (sizeParts.length > 0) facts.push(["Boyut", escapeHtml(sizeParts.join(" · "))]);
  if (product.CategoryPath && product.CategoryPath.length > 0) {
    facts.push(["Kategori", escapeHtml(product.CategoryPath.join(" › "))]);
  }
  if (product.Sku) facts.push(["Ürün Kodu", escapeHtml(product.Sku)]);
  if (offer) facts.push(["Kaynak", escapeHtml(offer.Source)]);

  const factsHtml = facts.length
    ? `<dl class="product-info-facts">
        ${facts.map(([label, value]) => `<div class="product-info-fact"><dt>${label}</dt><dd>${value}</dd></div>`).join("")}
      </dl>`
    : "";

  const listingTitleHtml = offer && offer.ExternalTitle
    ? `<div class="product-info-listing">
        <p class="product-info-listing-label">${escapeHtml(offer.Source)} sitesindeki başlık</p>
        <p class="product-info-listing-title">${escapeHtml(offer.ExternalTitle)}</p>
      </div>`
    : "";

  const attributes = product.Attributes || [];
  const attributesHtml = attributes.length
    ? `<dl class="product-info-facts">
        ${attributes.map((a) => `<div class="product-info-fact"><dt>${escapeHtml(a.Name)}</dt><dd>${escapeHtml((a.Values || []).join(", "))}</dd></div>`).join("")}
      </dl>`
    : "";

  // Specs (nutrition values, ingredients, dimensions, ...) come from the source store's own
  // product-detail page rather than a filter facet — see IProductSpecScraper on the backend —
  // so they're rendered as their own titled tables, one per SpecGroup, instead of folded into
  // the plain facts list above.
  const specGroups = product.Specs || [];
  const specsHtml = specGroups.length
    ? specGroups
        .map(
          (g) => `
      <div class="product-info-spec-group">
        <h4 class="product-info-spec-group-title">${escapeHtml(g.Group)}</h4>
        <dl class="product-info-facts">
          ${g.Items.map((i) => `<div class="product-info-fact"><dt>${escapeHtml(i.SpecName)}</dt><dd>${escapeHtml(i.SpecValue)}</dd></div>`).join("")}
        </dl>
      </div>`
        )
        .join("")
    : "";

  const displayImage = offer ? (offer.ImageUrl || null) : product.ImageUrl;
  const imageHtml = displayImage
    ? `<img src="${escapeHtml(displayImage)}" class="product-info-image" alt="${escapeHtml(offer?.ExternalTitle || product.CanonicalTitle)}">`
    : "";

  container.innerHTML = `
    ${imageHtml}
    ${factsHtml}
    ${listingTitleHtml}
    ${attributesHtml}
    ${specsHtml}
    ${!factsHtml && !listingTitleHtml && !attributesHtml && !specsHtml ? `<div class="state-message"><p>Bu ürün için ek bilgi bulunamadı.</p></div>` : ""}`;
}

function renderPriceHistoryChart(historyData, currentOffers) {
  const canvas = $("priceHistoryChart");
  if (!canvas || typeof Chart === "undefined") return;

  if (priceChartInstance) {
    priceChartInstance.destroy();
    priceChartInstance = null;
  }

  let points = [];
  if (Array.isArray(historyData) && historyData.length > 0) {
    points = historyData.map((h) => ({
      Date: h.Date || h.date,
      MinPrice: h.MinPrice ?? h.minPrice ?? h.Price ?? h.price
    }));
  }

  // If no history records exist or only 1 point, build a clean 2-point flat line (Yesterday & Today)
  if (points.length < 2 && currentOffers && currentOffers.length > 0) {
    const lowest = Math.min(...currentOffers.map((o) => o.Price));
    const todayStr = new Date().toISOString().split("T")[0];
    const yestDate = new Date(Date.now() - 86400000);
    const yestStr = yestDate.toISOString().split("T")[0];

    if (points.length === 1) {
      // If we have 1 point, use it as point 1 and today as point 2
      points = [
        { Date: points[0].Date, MinPrice: points[0].MinPrice },
        { Date: todayStr, MinPrice: lowest }
      ];
    } else {
      points = [
        { Date: yestStr, MinPrice: lowest },
        { Date: todayStr, MinPrice: lowest }
      ];
    }
  }

  if (points.length === 0) return;

  const labels = points.map((p) => {
    const d = new Date(p.Date);
    return isNaN(d.getTime()) ? p.Date : d.toLocaleDateString("tr-TR", { day: "numeric", month: "short" });
  });

  const prices = points.map((p) => p.MinPrice);

  const ctx = canvas.getContext("2d");
  const gradient = ctx.createLinearGradient(0, 0, 0, 160);
  gradient.addColorStop(0, "rgba(16, 185, 129, 0.3)");
  gradient.addColorStop(1, "rgba(16, 185, 129, 0.0)");

  priceChartInstance = new Chart(canvas, {
    type: "line",
    data: {
      labels: labels,
      datasets: [
        {
          label: "En Düşük Fiyat (₺)",
          data: prices,
          borderColor: "#10b981",
          borderWidth: 2.5,
          pointBackgroundColor: "#10b981",
          pointBorderColor: "#ffffff",
          pointBorderWidth: 2,
          pointRadius: 4,
          pointHoverRadius: 6,
          fill: true,
          backgroundColor: gradient,
          tension: 0.3
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          backgroundColor: "#0f172a",
          titleColor: "#94a3b8",
          bodyColor: "#10b981",
          bodyFont: { weight: "bold", size: 13 },
          padding: 8,
          cornerRadius: 6,
          displayColors: false,
          callbacks: {
            label: (ctx) => ` ${formatPrice(ctx.raw)}`
          }
        }
      },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: "#64748b", font: { size: 10 } }
        },
        y: {
          grid: { color: "rgba(226, 232, 240, 0.7)" },
          ticks: {
            color: "#64748b",
            font: { size: 10 },
            callback: (val) => formatPrice(val)
          }
        }
      }
    }
  });
}

function renderSimilarProductCard(p) {
  const card = document.createElement("div");
  card.className = "similar-product-card";
  card.setAttribute("role", "button");
  card.tabIndex = 0;

  const imageHtml = p.ImageUrl
    ? `<img src="${escapeHtml(p.ImageUrl)}" class="similar-product-image" alt="${escapeHtml(p.CanonicalTitle)}" loading="lazy">`
    : `<svg class="similar-product-image-placeholder" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
         <rect x="3" y="3" width="18" height="18" rx="2"/>
         <circle cx="8.5" cy="8.5" r="1.5"/>
         <path d="m21 15-5-5L5 21"/>
       </svg>`;

  card.innerHTML = `
    ${imageHtml}
    <div class="similar-product-info">
      <p class="similar-product-title">${escapeHtml(p.CanonicalTitle)}</p>
      <span class="similar-product-price">${formatPrice(p.LowestPrice)}</span>
      ${
        p.LowestPriceStore
          ? `<span class="price-store-badge">(${escapeHtml(p.LowestPriceStore)})</span>`
          : ""
      }
      ${formatUnitPrice(p.UnitPrice, p.UnitLabel)}
    </div>`;

  const open = () => openProductDetail(p.Id, p.CanonicalTitle);
  card.addEventListener("click", open);
  card.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      open();
    }
  });

  return card;
}

function renderSimilarProducts(container, products) {
  if (!products || products.length === 0) {
    container.innerHTML = `<div class="state-message"><p>Benzer ürün bulunamadı.</p></div>`;
    return;
  }

  container.innerHTML = "";

  const sameCategory = products.filter((p) => p.SameCategory);
  const related = products.filter((p) => !p.SameCategory);

  if (sameCategory.length > 0) {
    const heading = document.createElement("p");
    heading.className = "similar-group-heading";
    heading.textContent = "Aynı ürün grubu";
    container.appendChild(heading);
    sameCategory.forEach((p) => container.appendChild(renderSimilarProductCard(p)));
  }

  if (related.length > 0) {
    const heading = document.createElement("p");
    heading.className = "similar-group-heading";
    heading.textContent = "İlgili diğer ürünler";
    container.appendChild(heading);
    related.forEach((p) => container.appendChild(renderSimilarProductCard(p)));
  }
}

export function closeModal() {
  $("modal").classList.add("hidden");
  document.body.style.overflow = "";
}

export function initModal() {
  $("closeModal").addEventListener("click", closeModal);
  $("modal").querySelector(".modal-backdrop").addEventListener("click", closeModal);
}
