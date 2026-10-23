export { cn } from "cn"

// This project is explicitly single-currency (no multi-currency/i18n support, per
// requirements.md), so a fixed ZAR format is fine here rather than something configurable.
const currencyFormatter = new Intl.NumberFormat("en-ZA", {
  style: "currency",
  currency: "ZAR",
})

export function formatCurrency(amount: number): string {
  return currencyFormatter.format(amount)
}

/**
 * Converts a date-only string (`YYYY-MM-DD`, what every `<input type="date">` in this app
 * produces) into a full UTC ISO datetime string before it goes out over the wire. The backend's
 * `DueDate`/`DateFrom`/`DateTo` are `timestamp with time zone` columns, and Npgsql rejects a
 * `DateTime` with `Kind=Unspecified` — which is exactly what System.Text.Json/ASP.NET model
 * binding produce for a bare date-only string — with a 500, not a validation error. Sending an
 * explicit UTC instant instead (`...T00:00:00.000Z`) avoids that entirely.
 */
export function dateOnlyToUtcIso(dateOnly: string): string {
  return `${dateOnly}T00:00:00.000Z`
}

// Display-only: the backend persists and returns the literal role string "Owner" (see
// `architecture.md`'s UI display note), but end users should see it labeled "Admin". Never use
// this for comparisons — role checks must keep comparing against the raw "Owner"/"Staff" values.
export function displayRole(role: string): string {
  return role === "Owner" ? "Admin" : role
}
