function getApiBase() {
  if (typeof window !== "undefined" && window.location.protocol.startsWith("http")) {
    // If opened via local dev server (e.g. Live Server on 5500 / 3000 / 8080)
    if (window.location.port === "5500" || window.location.port === "3000" || window.location.port === "8080") {
      return "http://localhost:5196/api";
    }
    return `${window.location.origin}/api`;
  }
  return "http://localhost:5196/api";
}

export const API_BASE = getApiBase();

