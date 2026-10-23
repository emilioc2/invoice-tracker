import type { ReactNode } from "react";
import { Footer } from "@/components/layout/Footer";

/**
 * Wraps the entire route tree (both the `AppShell`-wrapped authenticated pages and the standalone
 * public auth pages) so `Footer` renders exactly once, pinned to the bottom of the viewport on
 * short-content pages rather than sitting wherever the routed content happens to end.
 *
 * The routed content is given `flex-1` here, and in turn every page it renders (`AppShell`'s own
 * shell div, and each standalone auth page's centering div) uses `flex-1` rather than its own
 * `min-h-screen` — stacking two independent `min-h-screen` elements would force the document to
 * be at least 2x viewport height, pushing the footer below the fold even when the page content
 * itself is short. `flex-1` lets this single outer `min-h-screen` be the only source of the
 * full-viewport-height guarantee, while still letting genuinely tall page content grow past it.
 */
export function RootLayout({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col">
      <div className="flex flex-1 flex-col">{children}</div>
      <Footer />
    </div>
  );
}
