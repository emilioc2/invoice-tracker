import { useEffect, useState } from "react";

// The app's single responsive breakpoint (architecture.md's "Mobile responsiveness" paragraph):
// below ~768px gets a mobile treatment (hamburger nav, card-per-row tables); at/above it, the
// existing desktop layout is unchanged, with no dedicated intermediate tablet layout. 767px (one
// below Tailwind's `md` breakpoint of 768px) is the exact inverse of `md:` so this hook and the
// app's Tailwind classes never disagree about which side of the line a given width falls on.
const MOBILE_BREAKPOINT_QUERY = "(max-width: 767px)";

function getIsMobile(): boolean {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return false;
  }
  return window.matchMedia(MOBILE_BREAKPOINT_QUERY).matches;
}

/**
 * Tracks whether the viewport is currently below the app's ~768px responsive breakpoint,
 * re-evaluating whenever the viewport crosses it (resize, orientation change, devtools device
 * emulation). Backed by `window.matchMedia` rather than a `resize` listener + manual width
 * comparison — fewer re-renders (only fires on an actual breakpoint crossing, not every pixel of
 * a drag-resize) and automatically correct if the breakpoint value above ever changes.
 *
 * Deliberately a single boolean, not a full media-query hook: this project has exactly one
 * breakpoint by design (no tablet-tuned layout), so a more general "useMediaQuery(query)" would
 * be speculative generality this app doesn't need.
 */
export function useIsMobile(): boolean {
  const [isMobile, setIsMobile] = useState(getIsMobile);

  useEffect(() => {
    if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
      return;
    }

    const mediaQueryList = window.matchMedia(MOBILE_BREAKPOINT_QUERY);
    const handleChange = () => setIsMobile(mediaQueryList.matches);

    // Reconcile immediately in case the query's result changed between the initial `useState`
    // evaluation (render time) and this effect running (commit time).
    handleChange();

    mediaQueryList.addEventListener("change", handleChange);
    return () => mediaQueryList.removeEventListener("change", handleChange);
  }, []);

  return isMobile;
}
