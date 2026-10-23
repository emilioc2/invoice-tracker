const SUPPORT_EMAIL = "emiliochanderdutt9@gmail.com";

/**
 * Minimal support-contact line shown on every page — both the `AppShell`-wrapped authenticated
 * pages and the standalone public auth pages (architecture.md's "Footer" paragraph). Deliberately
 * rendered once at the root of `App.tsx`'s route tree (see `RootLayout.tsx`) rather than inside
 * `AppShell` or duplicated per-page, and deliberately kept to a single line — no sitemap links, no
 * social icons, no multi-column layout.
 */
export function Footer() {
  return (
    <footer className="border-t border-border">
      <div className="mx-auto w-full max-w-6xl px-6 py-4 text-center text-sm text-muted-foreground">
        Need help?{" "}
        <a
          href={`mailto:${SUPPORT_EMAIL}`}
          className="underline-offset-2 hover:text-foreground hover:underline"
        >
          Contact support
        </a>
      </div>
    </footer>
  );
}
