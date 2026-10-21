// Base fetch wrapper used by every `api/*.ts` resource module. Never call `fetch` directly
// from a component or hook — go through `apiFetch` so JWT attachment and 401 handling stay
// centralized in one place.

const API_BASE_URL = (
  (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? "http://localhost:5100"
).replace(/\/+$/, "");

const TOKEN_STORAGE_KEY = "invoice-tracker.accessToken";

/** Read the persisted JWT. Exposed for AuthContext to rehydrate on mount. */
export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY);
}

export function setStoredToken(token: string | null): void {
  if (token) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token);
  } else {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
  }
}

/**
 * Called by `apiFetch` whenever the API returns 401. AuthContext registers its own logout
 * function here on mount so a 401 anywhere in the app clears the session and redirects to
 * `/login`, without `api/client.ts` needing to import AuthContext directly (which would be a
 * circular dependency — AuthContext itself calls through this client to log in).
 */
let unauthorizedHandler: (() => void) | null = null;

export function registerUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(message: string, status: number, body: unknown) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.body = body;
  }
}

export interface ApiFetchOptions extends Omit<RequestInit, "body"> {
  body?: unknown;
  /** Skip JWT attachment — only needed for login/register/forgot/reset-password. */
  skipAuth?: boolean;
}

/**
 * Shared request-sending/error-handling core for `apiFetch`/`apiFetchBlob`: attaches the JWT
 * (unless `skipAuth`), fires the 401 → logout hook, and throws a consistent `ApiError` (with a
 * JSON-parsed body where possible) on any non-2xx response. Callers get back the raw, still-ok
 * `Response` so they can each parse the body their own way (JSON vs. blob).
 */
async function sendRequest(path: string, options: ApiFetchOptions): Promise<Response> {
  const { body, skipAuth, headers, ...rest } = options;

  const finalHeaders = new Headers(headers);
  finalHeaders.set("Accept", "application/json");

  let finalBody: BodyInit | undefined;
  if (body !== undefined) {
    finalHeaders.set("Content-Type", "application/json");
    finalBody = JSON.stringify(body);
  }

  if (!skipAuth) {
    const token = getStoredToken();
    if (token) {
      finalHeaders.set("Authorization", `Bearer ${token}`);
    }
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...rest,
    headers: finalHeaders,
    body: finalBody,
  });

  if (response.status === 401) {
    // Only an authenticated request coming back 401 means "your session is no longer valid" —
    // a skipAuth request (login/register/forgot/reset-password) returning 401 means "bad
    // credentials," which has nothing to do with the current session and must not log it out.
    if (!skipAuth) {
      unauthorizedHandler?.();
    }
    const errorBody = await safeParseJson(response);
    throw new ApiError(
      `Request to ${path} failed with status ${response.status}`,
      401,
      errorBody,
    );
  }

  if (!response.ok) {
    const errorBody = await safeParseJson(response);
    throw new ApiError(
      `Request to ${path} failed with status ${response.status}`,
      response.status,
      errorBody,
    );
  }

  return response;
}

export async function apiFetch<TResponse>(
  path: string,
  options: ApiFetchOptions = {},
): Promise<TResponse> {
  const response = await sendRequest(path, options);

  if (response.status === 204) {
    return undefined as TResponse;
  }

  return (await safeParseJson(response)) as TResponse;
}

/**
 * Same JWT attachment/401 handling as `apiFetch`, but resolves to a `Blob` on success instead of
 * parsing JSON — needed for binary responses like `GET /api/invoices/{id}/pdf`, which isn't JSON
 * at all. A failed request still gets its (JSON) error body parsed the same way as `apiFetch`.
 */
export async function apiFetchBlob(
  path: string,
  options: ApiFetchOptions = {},
): Promise<Blob> {
  const response = await sendRequest(path, options);
  return response.blob();
}

async function safeParseJson(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}
