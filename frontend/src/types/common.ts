// Generic paginated-list envelope shared by every paginated backend endpoint (Clients today,
// Invoices shortly) — mirrors the backend's `PaginatedResult<T>` wrapper, camelCase on the wire.
export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
