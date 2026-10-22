# Business Invoice Tracker — Architecture Document

Companion to `requirements.md`. Captures internal structure and API design decisions.

## Backend solution layout

Clean/layered architecture: each layer only depends inward (API → Infrastructure → Application → Domain never the reverse).

```
backend/
├── src/
│   ├── InvoiceTracker.Domain/              # Entities, enums, domain logic — zero external dependencies
│   │   ├── Entities/                       # Business, User, Client, Invoice, InvoiceLineItem, Notification
│   │   ├── Enums/                          # InvoiceStatus (Draft/Unpaid/Paid/Overdue/Cancelled), UserRole (Owner/Staff)
│   │   └── Events/                         # InvoiceOverdueEvent (the Kafka payload's domain-side shape)
│   ├── InvoiceTracker.Application/          # Use cases + ports (interfaces), DTOs, validation
│   │   ├── Common/
│   │   │   ├── Interfaces/                 # IEmailSender, IEventPublisher, IPdfGenerator, ICurrentUserService
│   │   │   └── Behaviors/
│   │   │       └── ValidationBehavior.cs   # runs the matching FluentValidation validator before any handler executes
│   │   ├── Clients/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateClient/           # CreateClientCommand.cs, CreateClientCommandHandler.cs, CreateClientCommandValidator.cs
│   │   │   │   ├── UpdateClient/
│   │   │   │   └── ArchiveClient/
│   │   │   └── Queries/
│   │   │       ├── GetClientById/
│   │   │       └── ListClients/
│   │   ├── Invoices/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateInvoice/
│   │   │   │   ├── UpdateInvoice/
│   │   │   │   ├── SendInvoice/            # Draft → Unpaid
│   │   │   │   ├── MarkInvoicePaid/
│   │   │   │   ├── CancelInvoice/
│   │   │   │   └── ArchiveInvoice/
│   │   │   └── Queries/
│   │   │       ├── GetInvoiceById/
│   │   │       ├── ListInvoices/
│   │   │       └── GetDashboardSummary/
│   │   ├── Notifications/
│   │   │   └── Queries/ListNotifications/
│   │   ├── Users/
│   │   │   ├── Commands/ CreateStaffUser/, DeactivateUser/
│   │   │   └── Queries/ListUsers/
│   │   └── Auth/
│   │       └── Commands/ Register/, Login/, ForgotPassword/, ResetPassword/
│   ├── InvoiceTracker.Infrastructure/        # EF Core, Identity, Kafka, Formspree, QuestPDF — the adapters
│   │   ├── Persistence/                    # ApplicationDbContext, EF entity configs, Migrations
│   │   ├── Identity/                       # ASP.NET Core Identity wiring, JWT token generation
│   │   ├── Messaging/                      # KafkaEventPublisher, OverdueNotificationConsumer (hosted service)
│   │   ├── Email/                          # FormspreeEmailSender : IEmailSender
│   │   └── Pdf/                            # QuestPdfInvoiceGenerator : IPdfGenerator
│   └── InvoiceTracker.Api/                  # Controllers, composition root, Program.cs
│       ├── Controllers/                    # AuthController, UsersController, ClientsController, InvoicesController, NotificationsController, DashboardController
│       ├── BackgroundServices/             # OverdueInvoiceScannerService — periodic scan, flips Unpaid→Overdue, publishes event
│       └── Program.cs
├── tests/
│   ├── InvoiceTracker.UnitTests/           # Domain rules: status transitions, overdue calc, totals
│   └── InvoiceTracker.IntegrationTests/    # API endpoints against a real Postgres (Testcontainers)
├── Dockerfile
└── InvoiceTracker.sln
```

**UI display note:** the `UserRole.Owner` enum value shown above (and the `"Owner"` string persisted in the `Users.Role` column) is unchanged. The frontend now displays this role to end users as "Admin" — a presentation-layer label only, implemented entirely in frontend display strings. Every other reference to "Owner" in this document (API routes, role checks, test plan, etc.) refers to this same internal/persisted value.

**Indexing:** given the deliberately tight hosting budget (a single `t3.micro`), `Invoices` should carry a composite index on `(BusinessId, Status, DueDate)` — it's hit by the overdue scanner's periodic query, the filtered invoice list endpoint, and the dashboard summary. `Clients` and `Invoices` should also both index `(BusinessId, ArchivedAt)` since every list query filters on both.

## Domain invariants: centralized status transitions

The `Invoice` entity owns its own status machine. All status changes (`Draft→Unpaid`, `Unpaid→Overdue`, `Unpaid/Overdue→Paid`, `Draft/Unpaid→Cancelled` — per `requirements.md`'s status workflow) must go through a single domain method on `Invoice` (e.g. `TransitionTo(InvoiceStatus target)`) that internally guards which transitions are legal and rejects the rest. Command handlers (`SendInvoiceCommandHandler`, `MarkInvoicePaidCommandHandler`, `CancelInvoiceCommandHandler`) and `OverdueInvoiceScannerService` call this one method rather than each independently re-implementing "is this transition legal" checks. This keeps the workflow's rules in exactly one place instead of hand-duplicated across every caller, where they could otherwise silently drift out of sync.

## Request handling pattern: CQRS via MediatR

Every use case is a small, single-purpose command or query:

- A **command** (e.g. `CreateInvoiceCommand`) represents an action that changes data. A **query** (e.g. `ListInvoicesQuery`) represents a read. Each is just a data-holding class.
- Each command/query has exactly one **handler** class (e.g. `CreateInvoiceCommandHandler`) that contains the actual logic — touching the database, building entities, returning a result.
- Controllers never call application logic directly. They build a command/query object and hand it to MediatR:

```csharp
// InvoicesController
[HttpPost]
public async Task<IActionResult> Create(CreateInvoiceRequest request)
{
    var result = await _mediator.Send(new CreateInvoiceCommand(request.ClientId, request.LineItems, ...));
    return Ok(result);
}

// CreateInvoiceCommand.cs — just data
public record CreateInvoiceCommand(Guid ClientId, List<LineItemDto> LineItems, ...) : IRequest<InvoiceDto>;

// CreateInvoiceCommandHandler.cs — the actual work
public class CreateInvoiceCommandHandler : IRequestHandler<CreateInvoiceCommand, InvoiceDto>
{
    public async Task<InvoiceDto> Handle(CreateInvoiceCommand command, CancellationToken ct)
    {
        // validate, build the Invoice entity, save via DbContext, return a DTO
    }
}
```

MediatR finds the one handler registered for the command type and runs it — the controller stays thin and never references application services directly.

## Validation: FluentValidation as a MediatR pipeline behavior

Every command/query type can have a matching validator (e.g. `CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>`). A `ValidationBehavior` sits in MediatR's pipeline and runs the matching validator automatically before the handler executes — handlers never need to check "is this input valid," they only ever see validated input.

**Shared rules across validators:** when a validation rule is used by more than one validator, it belongs in one shared extension/rule set, not copy-pasted into each validator. Two concrete cases: password-strength rules (shared by `RegisterCommandValidator` and `ResetPasswordCommandValidator`) and pagination bounds — `PageNumber`/`PageSize` — (shared by `ListClientsQueryValidator`, `ListInvoicesQueryValidator`, and `ListUsersQueryValidator`) must each come from a single shared FluentValidation extension method (e.g. `RuleBuilderExtensions.PasswordRules()`, `.PaginationRules()`) applied by every validator that needs it, so the policy can't silently diverge between entry points.

Wiring (`Program.cs`):

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateInvoiceCommand).Assembly));
services.AddValidatorsFromAssembly(typeof(CreateInvoiceCommandValidator).Assembly);
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

## Multi-tenancy & authorization

Every query/command handler filters by the caller's `BusinessId`, resolved server-side from the JWT via `ICurrentUserService` — never taken from the request. If a client/invoice ID exists but belongs to a different business, the handler returns `404 Not Found`, not `403 Forbidden`, so a cross-tenant request can't even confirm the record exists.

Handlers that fetch a single entity by id for the current business (the `GetXById`/`UpdateX`/status-change pattern repeated across Clients, Invoices, Users, etc.) should use one shared helper for the "get by id scoped to `BusinessId`, or throw not-found" lookup, rather than each handler restating `.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId)` plus its own null-check-and-throw inline. A shared helper makes the multi-tenancy filter structurally consistent everywhere it's used instead of copy-pasted, directly serving the "never trust a client-supplied business ID" rule above.

Role checks (`Owner` vs `Staff`) are enforced per-endpoint for user-management operations — see the Users routes below.

## REST API routes

All routes require a JWT bearer token except `register`, `login`, `forgot-password`, and `reset-password`.

### Auth
| Method | Route | Notes |
|---|---|---|
| POST | `/api/auth/register` | Creates a new Business + its Owner user |
| POST | `/api/auth/login` | Returns a JWT |
| POST | `/api/auth/forgot-password` | Triggers reset email via `IEmailSender` |
| POST | `/api/auth/reset-password` | Consumes the reset token, sets new password |

### Users (team management — Owner only except where noted)
| Method | Route | Notes |
|---|---|---|
| GET | `/api/users` | List users in the caller's business — any authenticated user |
| POST | `/api/users` | Owner creates a Staff user (emails a temp password) |
| DELETE | `/api/users/{id}` | Owner deactivates a Staff user (sets `IsActive = false`), blocking future logins. An Owner cannot deactivate their own account — rejected with `400`. Note: this does not revoke a JWT already issued to the deactivated user — see `requirements.md`'s Auth row for this accepted limitation |

### Clients
| Method | Route | Notes |
|---|---|---|
| GET | `/api/clients` | List, with `?search=&pageNumber=&pageSize=` |
| GET | `/api/clients/{id}` | Single client |
| POST | `/api/clients` | Create |
| PUT | `/api/clients/{id}` | Update |
| DELETE | `/api/clients/{id}` | Archives (sets `ArchivedAt`), not a hard delete |

### Invoices
| Method | Route | Notes |
|---|---|---|
| GET | `/api/invoices` | List, with `?status=&clientId=&dateFrom=&dateTo=&pageNumber=&pageSize=` |
| GET | `/api/invoices/{id}` | Single invoice incl. line items |
| POST | `/api/invoices` | Create (always starts as `Draft`, with line items). `IssueDate` is not part of the create payload — it doesn't exist yet |
| PUT | `/api/invoices/{id}` | Update — only while `Draft`. Once sent, line items/due date/client are locked (mirrors a real invoice being a fixed record once issued); changes after sending mean `Cancel` + create a new one |
| POST | `/api/invoices/{id}/send` | `Draft → Unpaid`; sets `IssueDate = now` server-side (immutable from this point on — never directly editable via `PUT`, before or after sending) |
| POST | `/api/invoices/{id}/mark-paid` | `Unpaid/Overdue → Paid` |
| POST | `/api/invoices/{id}/cancel` | `Draft/Unpaid → Cancelled` |
| DELETE | `/api/invoices/{id}` | Archives (sets `ArchivedAt`) |
| GET | `/api/invoices/{id}/pdf` | Generates and streams back the invoice PDF |

### Notifications
| Method | Route | Notes |
|---|---|---|
| GET | `/api/notifications` | List, with optional `?invoiceId=` |

### Dashboard
| Method | Route | Notes |
|---|---|---|
| GET | `/api/dashboard/summary` | Outstanding total, overdue count, counts by status |

## API implementation notes

**Date/time handling (UTC):** every `DateTime` value exchanged with the API — in request bodies and in query parameters (e.g. `ListInvoices`'s `dateFrom`/`dateTo`) — must represent a true UTC instant. Where an incoming value carries an explicit UTC offset (anything other than `Z`), the API must convert it to UTC via `ToUniversalTime()`, not merely relabel its `Kind` as `Utc` via `SpecifyKind` — relabeling without converting silently shifts the represented instant whenever the offset is non-zero. This applies equally to the JSON converter used for request/response bodies (`UtcDateTimeJsonConverter`) and to query-string date binding (e.g. in `ListInvoicesQueryHandler`).

**List DTO shape — denormalized display fields:** `GET /api/invoices` and `GET /api/notifications` must return list DTOs that already include the fields the UI needs per row — client name on the invoice list DTO, invoice number on the notification list DTO — rather than only IDs that would force the frontend to fetch each related entity separately per row.

**PDF generation — single query:** `GET /api/invoices/{id}/pdf` should assemble the invoice (with line items), client, and business data needed for the template via one query with the necessary joins/projections (scoped by the current business), not three sequential round trips — the business lookup in particular has no dependency on the invoice query's result and shouldn't wait on it.

## Kafka: overdue-notification pipeline

Three hosted (`BackgroundService`) components, all running inside `InvoiceTracker.Api` (no separate deployable project) — each communicates with the others only via the database or the Kafka topic, never by direct method call, so they could be split into independent processes later with no code change beyond deployment config.

### 1. `OverdueInvoiceScannerService` — flags overdue invoices, writes an outbox entry

Runs on an interval (e.g. every 15 minutes). In a **single database transaction**:
1. Query `Invoices WHERE Status = Unpaid AND DueDate < now AND ArchivedAt IS NULL`
2. For each match: flip `Status → Overdue`, save
3. Insert an `OutboxMessage` row for the same invoice

Using the transactional outbox pattern here (rather than publishing to Kafka directly from the scanner) means the "this invoice is now overdue" fact and "an event needs to be published for it" fact are committed atomically. If the process crashes right after, nothing is lost — the outbox row is already durably recorded and will be picked up on the next relay pass, even though the invoice itself won't be re-selected by the scanner's query (it's no longer freshly overdue, but its outbox row is still unprocessed).

**OutboxMessage entity**: `Id, Type, Payload (JSON), OccurredAt, ProcessedAt (nullable)`

**Known limitation — single-instance only:** `OverdueInvoiceScannerService`'s scan-and-flip is not protected by any cross-instance mutual exclusion (no distributed/advisory lock). It assumes exactly one running instance of the API. This is consistent with the project's existing single-instance scope (one EC2 `t3.micro` running the full stack; the Kubernetes manifests are artifacts only, not a live cluster — see `requirements.md`). If the API is ever horizontally scaled, a distributed lock (or a Postgres advisory lock held for the duration of the scan) must be added around this step first — without it, two instances could race to flip and publish for the same invoice.

### 2. `OutboxRelayService` — publishes outbox entries to Kafka

Runs on a short interval (e.g. every 10-30 seconds):
1. Query `OutboxMessage WHERE ProcessedAt IS NULL`
2. For each: publish `Payload` to the `invoice-overdue` Kafka topic via `IEventPublisher`
3. On successful publish, set `ProcessedAt = now` and save

If Kafka is briefly unavailable, the relay simply retries on its next tick — the fact that the invoice went overdue is never lost, only the publish is delayed.

### 3. Event contract

- Topic: `invoice-overdue`
- Kafka message key: `InvoiceId` (keeps all events for one invoice on the same partition)
- Payload:
```json
{
  "eventId": "a3f1...",
  "eventType": "InvoiceOverdue",
  "occurredAt": "2026-10-19T09:00:00Z",
  "invoiceId": "b7e2...",
  "businessId": "c91a...",
  "invoiceNumber": "INV-0001",
  "clientId": "d44f...",
  "clientName": "Acme Co",
  "clientEmail": "billing@acme.co",
  "dueDate": "2026-10-12",
  "totalAmount": 1500.00
}
```

### 4. `OverdueNotificationConsumer` — sends and records the simulated notification, crash-safely

A naive "check if already processed, then send, then record" sequence has a gap: if the process crashes *after* sending the email but *before* recording the `Notification`, Kafka redelivers the event, the "already processed" check finds nothing, and the email is sent again. The fix is to record intent *before* the side effect, not after:

Subscribed to `invoice-overdue`. For each message:
1. Deserialize the event.
2. Look up a `Notification` by `SourceEventId`:
   - **Exists with `Status = Sent`:** already fully processed — log and skip (this is what handles Kafka's ordinary at-least-once redelivery after a successful run).
   - **Exists with `Status = Pending`:** a previous attempt crashed between recording and sending — proceed to step 3 using the existing row.
   - **Doesn't exist:** insert a new `Notification` row with `Status = Pending` (`InvoiceId`, `Channel = "Email"`, `Message`, `SourceEventId` = the event's `eventId`) *before* attempting to send.
3. Call `IEmailSender.SendAsync(...)` (the `FormspreeEmailSender` adapter). On success, update the row to `Status = Sent`, `SentAt = now`. On failure, update to `Status = Failed` (visible in the Notifications list; not automatically retried beyond Kafka's own redelivery of the underlying event).

Because the `Pending` row is written *before* the send is attempted, a crash can only ever leave a `Pending` row behind to retry — it can never produce a second `Sent` row for the same `SourceEventId`.

### Kafka topic configuration

Single-broker KRaft mode (no Zookeeper container) given this runs on one small VM: 1 partition (or 3 if headroom allows, for future horizontal scaling of the consumer), replication factor 1.

## Frontend architecture

**Stack:** React + TypeScript via Vite, React Router, **TanStack Query** for server state, **React Hook Form + Zod** for forms/validation, **Tailwind CSS + shadcn/ui** for styling/components.

```
frontend/
├── src/
│   ├── api/                        # typed fetch wrappers per resource — called by TanStack Query hooks, not by components directly
│   │   ├── client.ts                # base fetch wrapper — attaches JWT, handles 401 → logout
│   │   └── authApi.ts, clientsApi.ts, invoicesApi.ts, notificationsApi.ts, usersApi.ts, dashboardApi.ts
│   ├── types/                      # TS types mirroring backend DTOs
│   │   └── invoice.ts, client.ts, notification.ts, auth.ts
│   ├── schemas/                    # Zod validation schemas, one per form — same shape as the backend's FluentValidation validators
│   │   └── invoiceSchema.ts, clientSchema.ts, authSchemas.ts
│   ├── hooks/                      # TanStack Query hooks per resource
│   │   └── useClients.ts (useClientsQuery, useCreateClientMutation, ...), useInvoices.ts, useNotifications.ts, useAuth.ts, useDashboard.ts
│   ├── context/
│   │   └── AuthContext.tsx          # current user/business, login/logout, token storage
│   ├── routes/                     # page-level components
│   │   ├── LoginPage.tsx, RegisterPage.tsx, ForgotPasswordPage.tsx, ResetPasswordPage.tsx
│   │   ├── DashboardPage.tsx
│   │   ├── ClientsListPage.tsx, ClientDetailPage.tsx
│   │   ├── InvoicesListPage.tsx, InvoiceDetailPage.tsx, InvoiceFormPage.tsx  # create/edit, edit only while Draft
│   │   ├── NotificationsPage.tsx
│   │   └── UsersPage.tsx            # Owner-only team management
│   ├── components/
│   │   ├── ui/                      # shadcn/ui primitives (Button, Table, Dialog, Badge, DropdownMenu, ...) — generated into the project and owned as source, not a node_modules dependency
│   │   ├── layout/                  # AppShell (nav/header), ProtectedRoute
│   │   ├── invoices/                # InvoiceStatusBadge, InvoiceLineItemsEditor, InvoiceFilters
│   │   ├── clients/                 # ClientForm
│   │   └── common/                  # DataTable, Pagination, ConfirmDialog
│   ├── App.tsx                      # route definitions
│   └── main.tsx                     # entry point, wraps App in QueryClientProvider + AuthContext
├── components.json                  # shadcn/ui config
├── tailwind.config.ts
├── public/
├── index.html
├── vite.config.ts
└── Dockerfile
```

**Auth transitions and the TanStack Query cache:** because multiple businesses' users can log into the same browser tab over time, `AuthContext.tsx`'s `logout()` — both the explicit user-initiated one and the automatic one triggered by `api/client.ts`'s `401` handler — and the completion of a new `login()` must call `queryClient.clear()` (before updating auth state on logout, after updating it on login) to wipe every cached query. Without this, a second business's user could briefly see the first business's cached invoices, clients, notifications, or dashboard summary rendered from stale cache before any refetch completes. This is a hard requirement, not an optimization: multi-tenant data must never persist across a user switch in the same browser tab.

**Data-fetching pattern** — one hook file per resource wraps the matching `api/*.ts` functions in TanStack Query:

```ts
// hooks/useInvoices.ts
export function useInvoicesQuery(filters: InvoiceFilters) {
  return useQuery({ queryKey: ['invoices', filters], queryFn: () => invoicesApi.list(filters) });
}

export function useCreateInvoiceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: invoicesApi.create,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['invoices'] }),
  });
}
```

The example above shows only the `['invoices']` invalidation for brevity. In practice, **any mutation that changes an invoice's status or existence** — create, send, mark-paid, cancel, and archive — must invalidate both `['invoices']`-prefixed keys **and** `['dashboard', 'summary']`, since every one of those transitions changes the dashboard's per-status counts and/or outstanding total.

Pages call these hooks directly (`const { data, isLoading } = useInvoicesQuery(filters)`) — no manual loading/error state, no manual refetch-after-mutation wiring.

**Forms pattern** — a Zod schema defines both the TypeScript type and the validation rules, wired into React Hook Form via `zodResolver`:

```ts
// schemas/invoiceSchema.ts
export const invoiceSchema = z.object({
  clientId: z.string().uuid(),
  dueDate: z.coerce.date(),
  lineItems: z.array(z.object({ description: z.string().min(1), quantity: z.number().positive(), unitPrice: z.number().nonnegative() })).min(1),
});
export type InvoiceFormValues = z.infer<typeof invoiceSchema>;

// InvoiceFormPage.tsx
const form = useForm<InvoiceFormValues>({ resolver: zodResolver(invoiceSchema) });
```

## Test plan

### Backend unit tests (`InvoiceTracker.UnitTests` — xUnit, FluentAssertions, NSubstitute)

Pure domain/application logic, no database, no network:
- Status transitions — every valid transition succeeds (`Draft→Unpaid`, `Unpaid/Overdue→Paid`, `Draft/Unpaid→Cancelled`), every invalid one is rejected (e.g. `Paid→Cancelled`)
- Overdue detection — due yesterday + `Unpaid` → should flip; due today → should not; already `Paid`/`Cancelled`/`Overdue` → never reprocessed
- Totals calculation — `LineTotal = Quantity × UnitPrice`, `Subtotal` = sum of line totals, `TaxAmount = Subtotal × TaxRate`, `Total = Subtotal + TaxAmount`, including rounding behavior
- Invoice numbering format — `INV-0001` → `INV-0002` sequencing logic in isolation (concurrency-safety is covered at the integration level, below)
- FluentValidation validators — e.g. `CreateInvoiceCommandValidator` rejects empty line items, negative quantity/price, missing `ClientId`
- Command handlers — using EF Core's in-memory provider (fast, no real Postgres) rather than mocking `DbContext` directly: e.g. `MarkInvoicePaidCommandHandler` throws for an invoice not in `Unpaid`/`Overdue`
- Kafka pipeline components tested in isolation: `OverdueInvoiceScannerService`, `OutboxRelayService`, and `OverdueNotificationConsumer` each get unit tests against a mocked `IEventPublisher`/`IEmailSender` — no real Kafka broker in the test suite. For the consumer specifically: a case where no `Notification` exists (inserts `Pending`, sends, updates to `Sent`), a case where one exists as `Sent` (skips, never calls `IEmailSender`), and a case where one exists as `Pending` (retries the send using the existing row) — this last case is what proves the crash-safety design actually holds. End-to-end confidence that the full chain works together comes from manual verification against the real `docker-compose` stack, not an automated test.

### Backend integration tests (`InvoiceTracker.IntegrationTests` — xUnit, `WebApplicationFactory`, Testcontainers for Postgres)

Full HTTP pipeline, real database, real migrations:
- Auth — register creates Business+Owner; login succeeds/fails correctly; forgot/reset-password round trip (with a fake `IEmailSender` registered for tests, never a real Formspree call)
- Multi-tenancy isolation — Business A creates a client, an invoice, and (indirectly, via an overdue invoice) a notification; Business B's JWT requests each of the three → `404`, proving the BusinessId-scoping rule holds at the HTTP layer for all three resources, not just one
- Client & Invoice CRUD — including the Draft-only edit lock, and that archiving hides a record from the default list without deleting it
- Invoice numbering concurrency — fire concurrent create-invoice requests for the same business, assert no duplicate numbers (proves the row-locking counter approach under contention)
- Role-based authorization — Staff attempting `POST /api/users` → `403`; Owner succeeds; Owner attempting to deactivate their own account → `400`
- Dashboard summary — seed invoices across statuses, assert the numbers match
- PDF export — response is a valid PDF (content-type + `%PDF` magic bytes), not a visual/content assertion

### Frontend tests (Vitest, React Testing Library, MSW)

A handful of the highest-value cases, not exhaustive coverage:
- Invoice form validation (Zod/RHF) blocks submission on bad input, succeeds on valid input
- `InvoiceStatusBadge` renders the right label per status
- `ProtectedRoute` redirects when unauthenticated
- Invoice list status filter produces the right query params against a mocked API

## README outline

The actual `README.md`, written once implementation starts, will cover:

1. Overview — what the system does (one paragraph)
2. Tech stack summary (table, linking here for depth)
3. Architecture overview — brief diagram/description: React SPA → .NET API → Postgres + Kafka
4. Live deployment links (Vercel frontend, EC2-hosted API + Swagger) once deployed
5. Local development — prerequisites, `.env.example`, single `docker-compose up` to run the full stack, URLs to hit once it's up
6. Running tests — `dotnet test` (unit vs. integration, noting integration needs Docker running for Testcontainers), `npm test`
7. API documentation — Swagger/OpenAPI link
8. Database migrations — how they're applied (auto-applied on container startup, or manual `dotnet ef database update`)
9. Deployment — Vercel's git-integration for the frontend; GitHub Actions building/pushing the API image to GHCR, then how it's rolled out on the EC2 instance; note on the Kubernetes manifests being present but not live
10. CI/CD — what the GitHub Actions pipeline actually runs
11. **Design decisions & trade-offs** — the most important section: clean architecture + CQRS/MediatR, JWT+Identity over cookie auth, transactional outbox for Kafka publishing, KRaft mode Kafka, the Formspree delivery limitation, the `t3.micro` RAM trade-off and mitigations, why K8s manifests aren't live, Draft-only invoice editing, `ArchivedAt` over hard delete, per-business invoice numbering under concurrency, idempotent Kafka consumption via `SourceEventId`
12. Known limitations / possible future work — ties back to `requirements.md`'s out-of-scope list
13. Folder structure overview (brief, linking back to this document)

## Build order

A sequence that gets something runnable early and layers on from there:

1. **Repo & tooling scaffold** — solution + projects (Domain/Application/Infrastructure/Api/UnitTests/IntegrationTests), Vite+React+TS frontend scaffold, initial `docker-compose.yml` (Postgres + Kafka in KRaft mode)
2. **Domain + persistence foundation** — entities, enums, EF Core `DbContext` + configurations, first migration
3. **Auth** — Identity + JWT issuance, register/login, `ICurrentUserService` + BusinessId scoping, forgot/reset password (with a no-op `IEmailSender` for now)
4. **Core CRUD** — Clients and Invoices (incl. line items, per-business invoice numbering, status-transition endpoints, archive)
5. **Team management** — Owner-only Staff user creation/listing/deactivation
6. **Kafka pipeline** — `OutboxMessage` + migration, scanner, relay, Kafka topic, consumer, `FormspreeEmailSender`, Notifications endpoint
7. **PDF export** — QuestPDF invoice template + endpoint
8. **Dashboard summary endpoint**
9. **Backend tests** — unit (domain rules, handlers, validators, Kafka components mocked) then integration (Testcontainers Postgres, full HTTP flows)
10. **Frontend foundation** — Tailwind+shadcn setup, routing skeleton, `AuthContext`, API client with JWT attach + 401 handling, TanStack Query provider
11. **Frontend screens** — auth pages, then Clients, then Invoices (list/detail/form + status actions + PDF download), then Notifications/Dashboard/Users
12. **Frontend tests** — the handful specified in the test plan
13. **Dockerization** — multi-stage Dockerfiles (API, frontend), full `docker-compose.yml` wiring everything together
14. **Kubernetes manifests** (artifacts only — Deployment/Service/ConfigMap/Secret for API, Postgres, Kafka)
15. **CI/CD** — GitHub Actions workflow (backend build/test/image push); connect the repo to Vercel for the frontend
16. **Live deployment** — provision the EC2 instance, run the stack there, point Vercel's env var at its public address, verify the full flow end-to-end against the live system
17. **README + final polish**
