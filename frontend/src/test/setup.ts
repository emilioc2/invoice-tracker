import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, vi } from "vitest";
import { server } from "@/test/server";

// Radix UI's Select (used by InvoiceFormPage/InvoiceFilters) drives its open/close/selection
// behavior through the Pointer Events API and `scrollIntoView`, neither of which jsdom
// implements. Without these stubs, interacting with a Select in a test throws
// `TypeError: target.hasPointerCapture is not a function` before anything else runs.
beforeAll(() => {
  window.HTMLElement.prototype.hasPointerCapture ??= () => false;
  window.HTMLElement.prototype.setPointerCapture ??= () => {};
  window.HTMLElement.prototype.releasePointerCapture ??= () => {};
  window.HTMLElement.prototype.scrollIntoView ??= () => {};

  if (!window.ResizeObserver) {
    window.ResizeObserver = class ResizeObserver {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    };
  }

  // jsdom doesn't implement `window.matchMedia` at all (it's a layout API, and jsdom does no
  // layout). `useIsMobile` (the hook behind the app's one ~768px responsive breakpoint — see
  // architecture.md's "Mobile responsiveness" paragraph) calls it unconditionally, so every test
  // needs at least a stub or mounting `AppShell`/`DataTable` throws immediately. Defaulting
  // `matches` to `false` ("desktop") is deliberate: it keeps every pre-existing test's rendering
  // byte-for-byte identical to before this hook existed. A test that wants to exercise the mobile
  // layout overrides this per-test (e.g. `vi.spyOn(window, "matchMedia")...`).
  if (!window.matchMedia) {
    window.matchMedia = (query: string) =>
      ({
        matches: false,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(() => false),
      }) as unknown as MediaQueryList;
  }

  // Start the shared MSW server once for the whole suite; individual tests register handlers
  // with `server.use(...)`, and `onUnhandledRequest: "error"` makes an un-mocked request fail
  // the test loudly instead of silently resolving/rejecting in a confusing way.
  server.listen({ onUnhandledRequest: "error" });
});

afterEach(() => {
  server.resetHandlers();
  // RTL's auto-cleanup only self-registers when it detects jest/vitest globals; this project
  // deliberately imports `describe`/`it`/`expect` explicitly rather than enabling
  // `test.globals`, so cleanup is wired up by hand here instead — without it, every render()
  // across every test in a file piles up in the same `document.body`.
  cleanup();
});

afterAll(() => {
  server.close();
});
