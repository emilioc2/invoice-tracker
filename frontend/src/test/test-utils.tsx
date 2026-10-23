import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router-dom";
import { vi } from "vitest";
import { AuthProvider } from "@/context/AuthContext";
import type { AuthenticatedUser } from "@/types/auth";

// Must match the literal storage keys `api/client.ts`/`AuthContext.tsx` use — they're not
// exported, so these are kept in lockstep by hand rather than imported.
const TOKEN_STORAGE_KEY = "invoice-tracker.accessToken";
const USER_STORAGE_KEY = "invoice-tracker.user";

const DEFAULT_TEST_USER: AuthenticatedUser = {
  userId: "user-1",
  businessId: "business-1",
  email: "owner@example.com",
  role: "Owner",
  mustChangePassword: false,
};

/**
 * Seeds `localStorage` so `AuthProvider`'s lazy-initial-state read (on mount) rehydrates as
 * already logged in — mirrors a real user who authenticated in an earlier session, since the
 * screens under test here are normally only reachable through `ProtectedRoute` in the first
 * place.
 */
export function seedAuthenticatedSession(user: AuthenticatedUser = DEFAULT_TEST_USER): void {
  localStorage.setItem(TOKEN_STORAGE_KEY, "test-access-token");
  localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
}

/** Clears any persisted session — used to force the unauthenticated case deterministically. */
export function clearSession(): void {
  localStorage.removeItem(TOKEN_STORAGE_KEY);
  localStorage.removeItem(USER_STORAGE_KEY);
}

/** Fresh `QueryClient` per test, retries disabled so a mocked/failed request never retries and
 * times the test out. */
export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

/**
 * Mirrors `main.tsx`'s real provider nesting (`QueryClientProvider > AuthProvider > Router`), so
 * components written against the real `useAuth`/TanStack Query hooks behave the same way under
 * test. Callers compose their own `<Routes>`/`<Route>` tree as `ui` since each of the 4 cases
 * needs different routes/initial entries.
 */
export function renderWithProviders(ui: ReactElement, { route = "/" }: { route?: string } = {}) {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

/**
 * Mocks `window.matchMedia` so `useIsMobile` (the hook behind the app's one ~768px responsive
 * breakpoint — see `AppShell`/`DataTable`) reports the given viewport, since jsdom doesn't
 * evaluate real CSS/viewport width and `setup.ts`'s default stub always reports `matches: false`
 * ("desktop"). Call `mockViewport(true)` to exercise the mobile hamburger-nav/card-table layout
 * in a test; there's no need to call this at all for desktop-layout assertions, since that's
 * already every test's default.
 */
export function mockViewport(isMobile: boolean): void {
  vi.spyOn(window, "matchMedia").mockImplementation(
    (query: string) =>
      ({
        matches: isMobile,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(() => false),
      }) as unknown as MediaQueryList,
  );
}
