# Eternity Invoicing — Architecture Document

Companion to `requirements.md`. Captures internal structure and API design decisions.

## Backend solution layout

Clean/layered architecture: each layer only depends inward (API → Infrastructure → Application → Domain never the reverse).

```
backend/
├── src/
│   ├── InvoiceTracker.Domain/              # Entities, enums, domain logic — zero external dependencies
│   │   ├── Entities/                       # Business, UserBusinessMembership, Client, Invoice, InvoiceLineItem, Notification — UserBusinessMembership replaces the former 1:1 User entity; see "Multi-business membership" below. Business and Invoice each gain a nullable `PaymentDetails` string column — see "Business payment details" below
│   │   ├── Enums/                          # InvoiceStatus (Draft/Unpaid/Paid/Overdue/Cancelled), ClientStatus (Active/Removed), UserStatus (Active/Deactivated), UserRole (Owner/Staff), NotificationType (OverdueReminder/InvoiceSent)
│   │   └── Events/                         # InvoiceOverdueEvent (the Kafka payload's domain-side shape)
│   ├── InvoiceTracker.Application/          # Use cases + ports (interfaces), DTOs, validation
│   │   ├── Common/
│   │   │   ├── Interfaces/                 # IEmailSender, IEventPublisher, IPdfGenerator, IInvoicePdfService, ICurrentUserService
│   │   │   ├── Behaviors/
│   │   │   │   └── ValidationBehavior.cs   # runs the matching FluentValidation validator before any handler executes
│   │   │   ├── Models/                     # EmailAttachment (FileName, Content: byte[], ContentType) — passed to IEmailSender.SendAsync
│   │   │   └── Services/                   # InvoicePdfService : IInvoicePdfService — assembles Invoice+LineItems+Client+Business then calls IPdfGenerator; shared by GetInvoicePdfQueryHandler, SendInvoiceCommandHandler, ResendInvoiceEmailCommandHandler
│   │   ├── Clients/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateClient/           # CreateClientCommand.cs, CreateClientCommandHandler.cs, CreateClientCommandValidator.cs
│   │   │   │   ├── UpdateClient/
│   │   │   │   └── RemoveClient/
│   │   │   └── Queries/
│   │   │       ├── GetClientById/
│   │   │       └── ListClients/
│   │   ├── Invoices/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateInvoice/
│   │   │   │   ├── UpdateInvoice/
│   │   │   │   ├── SendInvoice/            # Draft → Unpaid
│   │   │   │   ├── ResendInvoiceEmail/        # re-sends the invoice email, no status transition
│   │   │   │   ├── MarkInvoicePaid/
│   │   │   │   ├── CancelInvoice/
│   │   │   │   └── ArchiveInvoice/
│   │   │   └── Queries/
│   │   │       ├── GetInvoiceById/
│   │   │       ├── ListInvoices/
│   │   │       └── GetDashboardSummary/
│   │   ├── Notifications/
│   │   │   └── Queries/ListNotifications/
│   │   ├── Business/                       # the business's own profile/settings — scoped to the caller's current business
│   │   │   ├── Commands/ UpdatePaymentDetails/
│   │   │   └── Queries/ GetBusinessProfile/
│   │   ├── Users/                          # operates on UserBusinessMembership rows, scoped to the caller's current business
│   │   │   ├── Commands/ CreateStaffUser/, DeactivateUser/, ReactivateUser/
│   │   │   └── Queries/ListUsers/
│   │   └── Auth/
│   │       └── Commands/ Register/, Login/, SelectBusiness/, SwitchBusiness/, AddBusiness/, ForgotPassword/, ResetPassword/
│   ├── InvoiceTracker.Infrastructure/        # EF Core, Identity, Kafka, Resend, QuestPDF — the adapters
│   │   ├── Persistence/                    # ApplicationDbContext, EF entity configs, Migrations
│   │   ├── Identity/                       # ASP.NET Core Identity wiring, JWT token generation
│   │   ├── Messaging/                      # KafkaEventPublisher, OverdueNotificationConsumer (hosted service)
│   │   ├── Email/                          # ResendEmailSender : IEmailSender (JSON POST with base64-encoded attachments, not multipart/form-data), NoOpEmailSender : IEmailSender (logs attachment filename/size) — ResendEmailSender is used when a real Resend API key is configured (any environment); NoOpEmailSender is the fallback otherwise, including Development without a key configured
│   │   └── Pdf/                            # QuestPdfInvoiceGenerator : IPdfGenerator
│   └── InvoiceTracker.Api/                  # Controllers, composition root, Program.cs
│       ├── Controllers/                    # AuthController, UsersController, ClientsController, InvoicesController, NotificationsController, DashboardController, BusinessController
│       ├── BackgroundServices/             # OverdueInvoiceScannerService — periodic scan, flips Unpaid→Overdue, publishes event
│       └── Program.cs
├── tests/
│   ├── InvoiceTracker.UnitTests/           # Domain rules: status transitions, overdue calc, totals
│   └── InvoiceTracker.IntegrationTests/    # API endpoints against a real Postgres (Testcontainers)
├── Dockerfile
└── InvoiceTracker.sln
```

**UI display note:** the `UserRole.Owner` enum value shown above (and the `"Owner"` string persisted in the `Users.Role` column) is unchanged. The frontend now displays this role to end users as "Admin" — a presentation-layer label only, implemented entirely in frontend display strings. Every other reference to "Owner" in this document (API routes, role checks, test plan, etc.) refers to this same internal/persisted value.

**Indexing:** given the deliberately tight hosting budget (a single `t3.micro`), `Invoices` should carry a composite index on `(BusinessId, Status, DueDate)` — it's hit by the overdue scanner's periodic query, the filtered invoice list endpoint, and the dashboard summary. `Clients` should index `(BusinessId, Status)` since every client list query filters on both. `Invoices` should additionally index `(BusinessId, ArchivedAt)` — Invoice's `ArchivedAt` remains separate from its `Status` workflow (see the domain invariants section below), so it needs its own index rather than being covered by the `(BusinessId, Status, DueDate)` index above.

## Domain invariants: centralized status transitions

Every entity with a lifecycle — `Invoice`, `Client`, and `User` — owns its own status machine via a single guarded domain method, `TransitionTo(TStatus target)`, that internally checks which transitions are legal from the entity's current status and rejects the rest. This is a project-wide convention, not an Invoice-specific one: command handlers and background services call this one method on the relevant entity rather than each independently re-implementing "is this transition legal" checks, which keeps each workflow's rules in exactly one place instead of hand-duplicated across every caller, where they could otherwise silently drift out of sync. Each entity's `Status` property setter is private — the only way to change it is through `TransitionTo`.

- **`Invoice.TransitionTo(InvoiceStatus target)`** — status changes (`Draft→Unpaid`, `Unpaid→Overdue`, `Unpaid/Overdue→Paid`, `Draft/Unpaid→Cancelled` — per `requirements.md`'s status workflow) are called from `SendInvoiceCommandHandler`, `MarkInvoicePaidCommandHandler`, `CancelInvoiceCommandHandler`, and `OverdueInvoiceScannerService`. Invoice's `ArchivedAt` (nullable timestamp) remains a separate, orthogonal concept from `Status` — archiving an invoice doesn't change where it sits in the Draft/Unpaid/Overdue/Paid/Cancelled workflow, so it is deliberately *not* folded into this transition machinery.
- **`Client.TransitionTo(ClientStatus target)`** — `ClientStatus` is `{ Active, Removed }`. The only legal transition is `Active → Removed` (user-facing "Remove"); there is no reverse transition — a removed client cannot currently be restored. Called from `RemoveClientCommandHandler`, replacing the former bare `client.ArchivedAt ??= DateTime.UtcNow` assignment.
- **`User.TransitionTo(UserStatus target)`** — `UserStatus` is `{ Active, Deactivated }`. Both directions are legal: `Active → Deactivated` (deactivate) and `Deactivated → Active` (reactivate). Called from `DeactivateUserCommandHandler` and the new `ReactivateUserCommandHandler`, replacing the former bare boolean flip on `IsActive`.

`Client` and `User` each also gain a `StatusChangedAt` column (`DateTime`, non-nullable), set by `TransitionTo` on every transition — kept separate from `LastModifiedDate`, which already exists on `Client` and bumps on any field edit, not specifically a status change, so it can't be relied on to answer "when did this entity's status last change."

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

Role checks (`Owner` vs `Staff`) are enforced per-endpoint for user-management operations — see the Users routes below. **Membership resolution:** every handler that used to resolve "the current user" as a single `Domain.User` row now resolves it as "the `UserBusinessMembership` for `ICurrentUserService.UserId` + `ICurrentUserService.BusinessId`" — both still come straight from the JWT's claims exactly as today, so `ICurrentUserService` itself requires no interface or implementation change. A route's `{id}` path parameter for user-management endpoints (`GET/DELETE/POST /api/users/{id}`) now identifies a `UserBusinessMembership.Id`, not a global identity id — a person with memberships in two businesses has two distinct ids, one per business, and each business only ever sees its own.

## REST API routes

All routes require a JWT bearer token except `register`, `login`, `forgot-password`, and `reset-password`.

### Auth
| Method | Route | Notes |
|---|---|---|
| POST | `/api/auth/register` | Creates a new identity (if the email has none yet) + a new Business + an Owner membership. `409 Conflict` if the email already has an identity — directs the caller to log in and use "Add a business" instead. See "Multi-business membership" |
| POST | `/api/auth/login` | `{ email, password }`. Exactly one active membership → returns a JWT + `MustChangePassword`, unchanged from before. More than one active membership → returns `{ requiresBusinessSelection: true, preAuthToken, memberships[] }` instead, no usable token yet. Zero active memberships → `401`, identical to bad credentials |
| POST | `/api/auth/select-business` | `{ preAuthToken, businessId }`. Completes a login that returned `requiresBusinessSelection: true` — re-validates the membership server-side, then returns the normal JWT response |
| POST | `/api/auth/switch-business` | Requires a valid JWT. `{ businessId }`. Re-validates the caller's identity holds an `Active` membership in the target business (`404` otherwise — never trusts the client-supplied id), then issues a brand-new JWT scoped to it. The previous token is not revoked |
| POST | `/api/auth/businesses` | Requires a valid JWT ("Add a business"). Creates a new Business + an Owner membership for the caller's existing identity (no new identity/password), then behaves like `switch-business` into it |
| GET | `/api/auth/memberships` | Requires a valid JWT. Returns the caller's own active memberships (`businessId`, `businessName`, `role`) — lets the frontend's business switcher do a real lazy fetch on open, rather than relying only on whatever `memberships` list a prior multi-membership login happened to return in this session |
| POST | `/api/auth/forgot-password` | Triggers reset email via `IEmailSender` |
| POST | `/api/auth/reset-password` | Consumes the reset token, sets new password |
| POST | `/api/auth/change-password` | Requires a valid JWT. Takes `{ newPassword }` — no `currentPassword` required, since the caller already holds a valid JWT issued at the moment they authenticated with the temp password; clears `MustChangePassword` on the caller's identity (not any one membership — see "Multi-business membership"). See "Forced password change" below |

**JWT claims, worked example:** a token issued for a person who is Owner of Business A and also Staff of Business B always carries exactly one `businessId` + one `role` claim — whichever business the token was issued for:
```json
// token issued via login or select-business for Business A
{ "sub": "4f1e...", "businessId": "a3c9...", "role": "Owner", "email": "sam@example.com", "jti": "..." }
// after switch-business to Business B — a brand-new token, not a mutation of the one above
{ "sub": "4f1e...", "businessId": "f02b...", "role": "Staff", "email": "sam@example.com", "jti": "..." }
```
`sub` is the person's identity id and never changes across a switch; `businessId`/`role` always describe exactly the one business this particular token is scoped to. There is no claim listing all of a person's memberships — that list is only ever returned in the `select-business`/login-selection response body and the explicit switch-business UI's own lookup, never baked into a bearer token.

### Business
| Method | Route | Notes |
|---|---|---|
| GET | `/api/business` | Returns the caller's current business profile (`Id, Name, Email, PaymentDetails`) — any authenticated user, no role restriction (same any-authenticated-user precedent as `GET /api/users`) |
| PUT | `/api/business/payment-details` | `{ paymentDetails }` (nullable string). Updates `Business.PaymentDetails` for the caller's business — any authenticated user (Owner or Staff), no `RequireRole` guard. A singular, non-plural route (`/api/business`, not `/api/businesses`) since a token is always scoped to exactly one business, matching the existing singular `Dashboard` pattern rather than introducing a separate `BusinessSettings` concept for what is currently a single field |

### Users (team management — Owner only except where noted)
| Method | Route | Notes |
|---|---|---|
| GET | `/api/users` | List users in the caller's business — any authenticated user (the frontend hides the Users nav tab/page from non-Owners, but this endpoint itself stays open to any authenticated user — a deliberate UI-only restriction, not a backend one) |
| POST | `/api/users` | Owner creates a Staff user (emails a temp password) |
| DELETE | `/api/users/{id}` | Owner deactivates a Staff user — calls `User.TransitionTo(UserStatus.Deactivated)`, blocking future logins. An Owner cannot deactivate their own account — rejected with `400`. Note: this does not revoke a JWT already issued to the deactivated user — see `requirements.md`'s Auth row for this accepted limitation |
| POST | `/api/users/{id}/reactivate` | Owner reactivates a previously deactivated Staff user — calls `User.TransitionTo(UserStatus.Active)`, restoring their ability to log in |

### Clients
| Method | Route | Notes |
|---|---|---|
| GET | `/api/clients` | List, with `?search=&pageNumber=&pageSize=` |
| GET | `/api/clients/{id}` | Single client |
| POST | `/api/clients` | Create |
| PUT | `/api/clients/{id}` | Update |
| DELETE | `/api/clients/{id}` | Removes the client — calls `Client.TransitionTo(ClientStatus.Removed)`, not a hard delete. Keeping `DELETE` as the verb matches Invoice's existing archive-via-`DELETE` pattern, even though neither is a literal hard delete |

### Invoices
| Method | Route | Notes |
|---|---|---|
| GET | `/api/invoices` | List, with `?status=&clientId=&dateFrom=&dateTo=&pageNumber=&pageSize=` |
| GET | `/api/invoices/{id}` | Single invoice incl. line items |
| POST | `/api/invoices` | Create (always starts as `Draft`, with line items). `IssueDate` is not part of the create payload — it doesn't exist yet |
| PUT | `/api/invoices/{id}` | Update — only while `Draft`. Once sent, line items/due date/client are locked (mirrors a real invoice being a fixed record once issued); changes after sending mean `Cancel` + create a new one |
| POST | `/api/invoices/{id}/send` | `Draft → Unpaid`; sets `IssueDate = now` server-side (immutable from this point on). Also attempts to email the client the invoice PDF + details. The status transition always succeeds independently of email outcome — response is `SendInvoiceResult` reporting `EmailDelivery.Status` (`Sent`/`Failed`) separately from the (unconditional, given valid input) status change |
| POST | `/api/invoices/{id}/resend-email` | Re-sends the invoice PDF + details email, no status transition. Valid for any status except `Draft` (`400` otherwise). Response is `ResendInvoiceEmailResult` reporting only the email delivery outcome |
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

**Security hardening (startup guard, rate limiting, HSTS):** three operational hardening measures, added following an external code review, sit in `Program.cs` alongside the pipeline above. (1) A startup guard (`JwtSecretGuard`) refuses to let the app start in any non-Development environment if the configured `Jwt:Secret` is missing or exactly matches the Development-only placeholder committed in `appsettings.Development.json` — otherwise every JWT in that environment would be signed with a secret that's public in this repo's history. (2) The brute-force-sensitive auth actions (`login`, `register`, `forgot-password`, `reset-password`, `change-password`) carry `[EnableRateLimiting("auth")]`, backed by a fixed-window limiter partitioned per client IP and configured via `RateLimiting:Auth:PermitLimit`/`WindowSeconds` — 10 requests/60 seconds in `appsettings.json`'s production default, rejecting the excess with a plain `429` (no queueing). `appsettings.Development.json` overrides this with a much higher limit so local dev and the integration test suite's many rapid register/login calls never trip it; the dedicated rate-limiter integration test builds its own short-lived `WithWebHostBuilder` host with a tiny limit instead of relying on that shared config. (3) `UseHsts()` runs before `UseHttpsRedirection()` whenever the environment isn't Development, matching ASP.NET Core's documented ordering.

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

The payload itself stays exactly as above — lightweight, summary fields only, no line items — even though the consumer (below) now does more with it than it used to. The PDF attachment and full invoice detail come from the consumer's own DB-backed lookup via the event's `invoiceId`/`businessId`, not from new fields added to the event. This keeps the event contract decoupled from the email template: changing the reminder's wording, or what gets attached, never requires a payload schema change or coordination with the producer side (the scanner/relay).

### 4. `OverdueNotificationConsumer` — sends and records the simulated notification, crash-safely

A naive "check if already processed, then send, then record" sequence has a gap: if the process crashes *after* sending the email but *before* recording the `Notification`, Kafka redelivers the event, the "already processed" check finds nothing, and the email is sent again. The fix is to record intent *before* the side effect, not after:

Subscribed to `invoice-overdue`. For each message:
1. Deserialize the event.
2. Look up a `Notification` by `SourceEventId`:
   - **Exists with `Status = Sent`:** already fully processed — log and skip (this is what handles Kafka's ordinary at-least-once redelivery after a successful run).
   - **Exists with `Status = Pending`:** a previous attempt crashed between recording and sending — proceed to step 3 using the existing row.
   - **Doesn't exist:** insert a new `Notification` row with `Status = Pending` (`InvoiceId`, `Channel = "Email"`, `Message`, `SourceEventId` = the event's `eventId`) *before* attempting to send.
3. Load the full invoice for PDF generation: call `IInvoicePdfService.GenerateAsync(invoiceId, businessId, ct)` using the `invoiceId`/`businessId` already present on the event. This resolves Invoice+LineItems+Client+Business via the consumer's scoped `IApplicationDbContext` — the same assembly `InvoicePdfService` already performs for the Send/Resend path (see "Invoice send/resend email delivery" below). If generation itself throws (e.g. the invoice can no longer be loaded), treat it the same as a send failure in step 4 — update the row to `Status = Failed` rather than leaving it `Pending` indefinitely.
4. Build the subject/body (see "Overdue reminder email content" below) and call `IEmailSender.SendAsync(client.Email, subject, body, [pdfAttachment], ct)` (the `ResendEmailSender` adapter). On success, update the row to `Status = Sent`, `SentAt = now`. On failure, update to `Status = Failed` (visible in the Notifications list; not automatically retried beyond Kafka's own redelivery of the underlying event).

**Overdue reminder email content:** today's bare one-line message (`"Invoice {number} for {client} is overdue (due {date}, total {amount})."`, no greeting, no attachment) is replaced with a proper letter, structured the same way as the Send/Resend invoice email — greeting, body content, closing — but worded for an overdue reminder rather than "here's your invoice":

- Subject: `Invoice {number} is overdue`
- Body: `Dear {client},\n\nThis is a reminder that invoice {number} is now overdue.\n\nAmount due: {total}\nDue date: {date}\n\nPlease arrange payment as soon as possible. A copy of the invoice is attached for your reference.\n\nThank you.`
- Attachment: the invoice PDF from step 3, same as the Send/Resend flow's `pdfAttachment`.

Whether this is added as a new method on the existing `InvoiceEmailContentBuilder` (e.g. `BuildOverdueReminder(...)` alongside its Send/Resend method) or a small sibling builder is an implementation detail left to the developer's judgment — either way, the overdue and Send/Resend templates should stay visibly related (same greeting/closing conventions, same construction from Invoice+LineItems+Client+Business) so a future wording change to one doesn't quietly drift from the other.

Because the `Pending` row is written *before* the send is attempted, a crash can only ever leave a `Pending` row behind to retry — it can never produce a second `Sent` row for the same `SourceEventId`.

### Kafka topic configuration

Single-broker KRaft mode (no Zookeeper container) given this runs on one small VM: 1 partition (or 3 if headroom allows, for future horizontal scaling of the consumer), replication factor 1.

## Invoice send/resend email delivery

Unlike the overdue-reminder pipeline, invoice Send and Resend are **foreground, user-initiated actions**, so they skip the outbox+Kafka pattern and call `IEmailSender.SendAsync(...)` directly from the command handler — the same pattern already used by `ForgotPasswordCommandHandler`/`CreateStaffUserCommandHandler`. Outbox+Kafka's crash-safety machinery exists to make an *unattended background job* (the overdue scanner) safe; here, a failure surfaces synchronously to the user, who can just retry (via Resend), so the added complexity isn't justified.

**`IEmailSender` interface change:**
```csharp
public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string body, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct);
}

public record EmailAttachment(string FileName, byte[] Content, string ContentType);
```
Existing callers with no attachment (`ForgotPasswordCommandHandler`, `CreateStaffUserCommandHandler`) pass `Array.Empty<EmailAttachment>()`. `ResendEmailSender` sends attachments as base64-encoded content in the same JSON payload as the rest of the message — simpler than a multipart upload; `NoOpEmailSender` logs each attachment's filename and byte count instead of sending; any test double implementing `IEmailSender` needs the new parameter.

**`IInvoicePdfService` (new, Application-layer):**
```csharp
public interface IInvoicePdfService
{
    Task<byte[]> GenerateAsync(Guid invoiceId, Guid businessId, CancellationToken ct);
}
```
Implemented in `Application/Common/Services/` (not Infrastructure — it only orchestrates `IApplicationDbContext` for the Invoice+LineItems+Client+Business assembly query and `IPdfGenerator` for rendering, both of which are already Application-layer-visible dependencies). `GetInvoicePdfQueryHandler`, `SendInvoiceCommandHandler`, and `ResendInvoiceEmailCommandHandler` all depend on this instead of each assembling the query themselves.

**`SendInvoiceCommandHandler` sequence (replaces the plain status-flip):**
1. Load the invoice (`BusinessId`-scoped), call `Invoice.TransitionTo(InvoiceStatus.Unpaid)` (sets `IssueDate = now` as already specified), save. This is the transition proper — if it fails (e.g. invoice not in `Draft`), the whole command fails as it always has, and nothing below runs.
2. Once committed, call `IInvoicePdfService.GenerateAsync(...)`.
3. Insert a `Notification` row (`Status = Pending`, `NotificationType = InvoiceSent`, `SourceEventId = Guid.NewGuid()`), then call `IEmailSender.SendAsync(client.Email, subject, bodyWithInvoiceDetails, [pdfAttachment], ct)`.
4. On success, update the row to `Sent`, `SentAt = now`; on failure (exception or non-success result), update to `Failed`. Either outcome is returned alongside the (already-committed) invoice — the email step never rolls back step 1.

Result shape:
```csharp
public record EmailDeliveryResult(string Status, Guid NotificationId); // "Sent" | "Failed"
public record SendInvoiceResult(InvoiceDto Invoice, EmailDeliveryResult EmailDelivery);
```

**`ResendInvoiceEmailCommandHandler` (new):**
1. Load the invoice (`BusinessId`-scoped, via the shared lookup helper). If `Status == Draft`, reject with `400` — nothing to resend yet.
2. Call `IInvoicePdfService.GenerateAsync(...)`.
3. Insert a new `Notification` row (`Pending`, `NotificationType = InvoiceSent`, fresh `SourceEventId`), call `IEmailSender.SendAsync(...)`, update to `Sent`/`Failed` — identical pattern to Send's steps 3-4, with no preceding status transition and no interaction with the Draft-only edit lock (resending never touches invoice content).

Result shape: `public record ResendInvoiceEmailResult(EmailDeliveryResult EmailDelivery);`

## Business payment details

**Migration:** adds two nullable string columns — `Businesses.PaymentDetails` and `Invoices.PaymentDetails`. No backfill needed; existing rows default to `null`, which renders as "nothing" everywhere this field is read (see below).

**`GetBusinessProfileQueryHandler` / `UpdatePaymentDetailsCommandHandler` (new, `Application/Business/`):** both resolve the target business as `ICurrentUserService.BusinessId`, never a client-supplied id — the same multi-tenancy rule as every other handler. `UpdatePaymentDetailsCommandHandler` takes `{ PaymentDetails: string? }` and writes it straight to `Business.PaymentDetails`; no FluentValidation rule beyond the nullable-string shape itself is needed, consistent with how `Invoice.Notes` also has no documented length/format constraint. Neither handler performs a role check (`Owner` vs `Staff`) — both are reachable by any authenticated member of the caller's business, per `requirements.md`'s "Business payment details" section and mirroring the existing `GET /api/users` any-authenticated-user precedent. `BusinessController` exposes these as `GET /api/business` and `PUT /api/business/payment-details` (see REST API routes above).

**`CreateInvoiceCommandHandler` — resolving `Invoice.PaymentDetails`:** when the create request includes an explicit `paymentDetails` override, that value is used as-is. Otherwise, the handler reads the caller's `Business.PaymentDetails` (same `BusinessId`-scoped lookup already used for every other business-scoped read) and copies its current value onto the new `Invoice.PaymentDetails` at the moment of creation — a one-time snapshot, not a live reference. A later change to `Business.PaymentDetails` never retroactively touches invoices already created, the same "fixed once set" treatment this document already applies to `Invoice.IssueDate` once an invoice is sent.

**`UpdateInvoiceCommandHandler` — editing `Invoice.PaymentDetails`:** treated exactly like the handler's other Draft-only-editable fields (line items, due date, client) — the existing Draft-only edit lock already documented for `PUT /api/invoices/{id}` applies unchanged: the field can be changed while the invoice is still `Draft`, and is rejected (along with the rest of the payload) once the invoice has moved past `Draft`. No special-casing is introduced for this field relative to the lock that already governs the rest of the invoice.

**`QuestPdfInvoiceGenerator` — rendering:** a new section is added near the bottom of the invoice template, after the line items/totals block, rendered only when `Invoice.PaymentDetails` is non-empty — a small "Payment Details" heading followed by the free-text value. When `Invoice.PaymentDetails` is null/empty, the section (heading included) is omitted entirely rather than rendered empty.

**`InvoiceEmailContentBuilder` — `Build(...)` and `BuildOverdueReminder(...)`:** both methods (Send/Resend's `Build(...)` and the overdue reminder's `BuildOverdueReminder(...)`, added alongside it per the "Overdue reminder email content" section above) append a payment-details line to the body — placed with the other invoice details, before the closing line — whenever the invoice's `PaymentDetails` is non-empty. When it's null/empty, neither method adds anything, so the body is byte-for-byte what it was before this feature. This applies the payment-details line to both email bodies, not just the PDF attachment, per `requirements.md`'s "Invoice email delivery" section.

## Forced password change for temp-password logins

A Staff user's temp password (created by `CreateStaffUserCommandHandler` — see `requirements.md`'s "Multi-user onboarding") must be changed before that user can use the rest of the system. This is a new, mid-session flow — distinct from the existing `forgot-password`/`reset-password` pair, which remains a pre-auth, reset-token-based flow for "I don't remember my password at all." The forced-change flow instead assumes the caller already holds a valid JWT and knows their current (temp) password.

**`MustChangePassword` (bool, default `false`) — new column on the identity (`ApplicationUser`), needs an EF Core migration.**
- Set `true` by `CreateStaffUserCommandHandler` at the same point it generates and emails the temp password.
- Cleared to `false` by the new `ChangePasswordCommandHandler` (below) on a successful change.

**`AuthResultDto` gains a `MustChangePassword` field:**
```csharp
public record AuthResultDto(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid UserId,
    Guid BusinessId,
    string Email,
    string Role,
    bool MustChangePassword);
```
Returned by `POST /api/auth/login`. `RegisterCommandHandler` returns the same `AuthResultDto` shape (`register` creates the Owner and logs them in immediately) — `MustChangePassword` is always `false` there, since a newly-registered Owner chooses their own password at registration and never has a temp password.

**`ChangePasswordCommandHandler` (new, `Auth/Commands/ChangePassword/`):** takes only `{ newPassword }` from the authenticated caller (`ICurrentUserService` resolves the user, never a client-supplied id) — no `currentPassword` field, and no verification step against it. The caller already holds a valid JWT issued at the moment they authenticated with the temp password moments earlier, which is proof enough for this mid-session change; re-verifying the password they just logged in with adds no additional security.
1. Load the current user via `ICurrentUserService`.
2. Set the new password directly, without needing or verifying the old one: `UserManager.RemovePasswordAsync(user)` followed by `UserManager.AddPasswordAsync(user, newPassword)` (rather than `UserManager.ChangePasswordAsync`, which requires the old password).
3. On success, set `MustChangePassword = false`, save.

`ChangePasswordCommandValidator` applies the same shared `RuleBuilderExtensions.PasswordRules()` extension already used by `RegisterCommandValidator` and `ResetPasswordCommandValidator` (per this document's "shared rules across validators" convention above) — the password-strength policy is not re-implemented a third time.

No JWT re-issuance happens here and none is needed: the already-issued JWT remains valid and usable to call `change-password`, because `MustChangePassword` was never a JWT claim — it's response-body/DB state only, consistent with the project's existing minimal-JWT, no-refresh-token design (see `requirements.md`'s Auth row).

## Frontend architecture

**Stack:** React + TypeScript via Vite, React Router, **TanStack Query** for server state, **React Hook Form + Zod** for forms/validation, **Tailwind CSS + shadcn/ui** for styling/components.

```
frontend/
├── src/
│   ├── api/                        # typed fetch wrappers per resource — called by TanStack Query hooks, not by components directly
│   │   ├── client.ts                # base fetch wrapper — attaches JWT, handles 401 → logout
│   │   └── authApi.ts, clientsApi.ts, invoicesApi.ts, notificationsApi.ts, usersApi.ts, dashboardApi.ts, businessApi.ts
│   ├── types/                      # TS types mirroring backend DTOs
│   │   └── invoice.ts, client.ts, notification.ts, auth.ts, business.ts
│   ├── schemas/                    # Zod validation schemas, one per form — same shape as the backend's FluentValidation validators
│   │   └── invoiceSchema.ts, clientSchema.ts, authSchemas.ts
│   ├── hooks/                      # TanStack Query hooks per resource
│   │   └── useClients.ts (useClientsQuery, useCreateClientMutation, ...), useInvoices.ts, useNotifications.ts, useAuth.ts, useDashboard.ts, useBusiness.ts (useBusinessProfileQuery, useUpdatePaymentDetailsMutation — the mutation invalidates the `['business']` query key on success, the same invalidate-on-mutate pattern every other resource hook already follows)
│   ├── context/
│   │   └── AuthContext.tsx          # current user/business, login/logout, token storage
│   ├── routes/                     # page-level components
│   │   ├── LoginPage.tsx, RegisterPage.tsx, ForgotPasswordPage.tsx, ResetPasswordPage.tsx, ChangePasswordPage.tsx
│   │   ├── DashboardPage.tsx
│   │   ├── ClientsListPage.tsx, ClientDetailPage.tsx
│   │   ├── InvoicesListPage.tsx, InvoiceDetailPage.tsx, InvoiceFormPage.tsx  # create/edit, edit only while Draft
│   │   ├── NotificationsPage.tsx
│   │   ├── UsersPage.tsx            # Owner-only team management
│   │   └── SettingsPage.tsx         # business profile/payment-details settings — visible to all authenticated users, not role-filtered (unlike UsersPage)
│   ├── components/
│   │   ├── ui/                      # shadcn/ui primitives (Button, Table, Dialog, Badge, DropdownMenu, ...) — generated into the project and owned as source, not a node_modules dependency
│   │   ├── layout/                  # AppShell (nav/header), ProtectedRoute, Footer (new — simple support-contact line, "Need help? Contact support@..." with a `mailto:emiliochanderdutt9@gmail.com` link; rendered once at the root of `App.tsx`'s route tree rather than inside `AppShell`, so it shows on both the authenticated pages `AppShell` wraps (Dashboard/Clients/Invoices/Users/Notifications/Settings) and the standalone public auth pages (Login/Register/ForgotPassword/ResetPassword/ChangePassword) that currently render outside `AppShell` — never duplicated per-page)
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

**New route and nav entry for Settings:** `App.tsx` adds a `/settings` route, rendered inside `AppShell` like every other authenticated page, pointing at the new `SettingsPage.tsx`. `AppShell.tsx`'s `navItems` gains a corresponding "Settings" entry — unlike the "Users" entry, which `AppShell` filters out for non-Owner roles, the "Settings" entry is shown to every authenticated user regardless of role, matching the "any authenticated business member" access rule from `requirements.md`'s "Business payment details" section.

**Footer:** `App.tsx` renders the new `Footer.tsx` (see `components/layout/` above) once, outside/beneath whichever route tree is active, so it appears under both the `AppShell`-wrapped authenticated routes and the standalone public auth routes without being added to either layout individually.

**Auth transitions and the TanStack Query cache:** because multiple businesses' users can log into the same browser tab over time, `AuthContext.tsx`'s `logout()` — both the explicit user-initiated one and the automatic one triggered by `api/client.ts`'s `401` handler — and the completion of a new `login()` must call `queryClient.clear()` (before updating auth state on logout, after updating it on login) to wipe every cached query. Without this, a second business's user could briefly see the first business's cached invoices, clients, notifications, or dashboard summary rendered from stale cache before any refetch completes. This is a hard requirement, not an optimization: multi-tenant data must never persist across a user switch in the same browser tab.

**Business-selector and switcher UI:** when `login()`'s response has `requiresBusinessSelection: true`, `AuthContext.tsx` must not persist a token or user yet — it hands the `preAuthToken` + `memberships` list to a new `SelectBusinessPage.tsx`, which calls `authApi.selectBusiness(preAuthToken, businessId)` on selection; that call's success is treated exactly like a normal `login()` completion (persist token/user, `queryClient.clear()`). Separately, an authenticated person whose identity holds more than one active membership gets a "Switch business" affordance (e.g. in `AppShell`'s header, next to the current business name) that lists their other active memberships (fetched lazily, only when opened) and calls a new `switchBusiness(businessId)` on selection — which must run through the same state-update-plus-`queryClient.clear()` sequence as `login`/`logout`. The existing comment above ("because multiple businesses' users can log into the same browser tab over time") describes *different people* logging in sequentially in one tab; it is extended, not superseded, by this feature — there are now four auth-transition points that must each clear the cache: `login`, `logout`, `selectBusiness`, and `switchBusiness`.

**Forced password-change route guard:** when `login()` resolves with `mustChangePassword: true`, `AuthContext.tsx` routes the SPA to a new `ChangePasswordPage.tsx` and keeps it there — the inverse of the existing `ProtectedRoute` (`components/layout/ProtectedRoute.tsx`), which keeps unauthenticated users *out* of the app; this guard keeps an authenticated-but-not-yet-compliant user *out of everything else*, until the `POST /api/auth/change-password` call succeeds and clears the flag in client state, at which point normal routing resumes with no new login required.

**Mobile responsiveness:** the frontend supports exactly one responsive breakpoint, ~768px — below it, a mobile treatment applies; at and above it, the existing desktop layout is unchanged, with no dedicated intermediate tablet layout (tablet-width screens inherit the desktop layout as-is). `AppShell.tsx` collapses its header nav into a hamburger/drawer menu below the breakpoint, in place of the unconditional horizontal nav row it renders today. `DataTable.tsx` renders a card-per-row layout below the breakpoint instead of its current table layout; because `DataTable` is generic over column config, this isn't automatic from the existing column config alone — each calling page (`ClientsListPage`, `InvoicesListPage`, `UsersPage`, `NotificationsPage`) must additionally specify which fields surface in its card view. Forms and dialogs (`ClientForm`, the invoice line-item editor, `AddBusinessForm`, and other dialog-based forms) get a pass for input sizing and touch-target spacing (44×44px minimum) as part of the same effort, not deferred to a later one.

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
- Status transitions — every valid transition succeeds (`Draft→Unpaid`, `Unpaid/Overdue→Paid`, `Draft/Unpaid→Cancelled`), every invalid one is rejected (e.g. `Paid→Cancelled`) for `Invoice`; the same pattern for `Client` (`Active→Removed` succeeds, there is no reverse transition to test) and `User` (`Active→Deactivated` and `Deactivated→Active` both succeed, since `UserStatus` is explicitly bidirectional)
- Overdue detection — due yesterday + `Unpaid` → should flip; due today → should not; already `Paid`/`Cancelled`/`Overdue` → never reprocessed
- Totals calculation — `LineTotal = Quantity × UnitPrice`, `Subtotal` = sum of line totals, `TaxAmount = Subtotal × TaxRate`, `Total = Subtotal + TaxAmount`, including rounding behavior
- Invoice numbering format — `INV-0001` → `INV-0002` sequencing logic in isolation (concurrency-safety is covered at the integration level, below)
- FluentValidation validators — e.g. `CreateInvoiceCommandValidator` rejects empty line items, negative quantity/price, missing `ClientId`
- Command handlers — using EF Core's in-memory provider (fast, no real Postgres) rather than mocking `DbContext` directly: e.g. `MarkInvoicePaidCommandHandler` throws for an invoice not in `Unpaid`/`Overdue`
- Kafka pipeline components tested in isolation: `OverdueInvoiceScannerService`, `OutboxRelayService`, and `OverdueNotificationConsumer` each get unit tests against a mocked `IEventPublisher`/`IEmailSender` — no real Kafka broker in the test suite. For the consumer specifically: a case where no `Notification` exists (inserts `Pending`, sends, updates to `Sent`), a case where one exists as `Sent` (skips, never calls `IEmailSender`), and a case where one exists as `Pending` (retries the send using the existing row) — this last case is what proves the crash-safety design actually holds. Now that the consumer also attaches the invoice PDF and sends a letter-format body, these same three cases need extending (not just asserting `SendAsync` was called): assert `IInvoicePdfService.GenerateAsync` is invoked with the event's `invoiceId`/`businessId` and its result is passed through as the email's attachment, and assert the subject/body match the overdue reminder template (greeting, amount due, due date, closing) rather than the old one-line message. Add a further case covering `IInvoicePdfService.GenerateAsync` throwing (e.g. invoice no longer loadable) — that should land the `Notification` row as `Failed`, not left `Pending`. End-to-end confidence that the full chain works together comes from manual verification against the real `docker-compose` stack, not an automated test.
- `SendInvoiceCommandHandler`: status transition still succeeds and persists when `IEmailSender` throws/returns failure; `Notification` recorded as `Sent` on success and `Failed` on failure, in both cases `NotificationType = InvoiceSent`
- `ResendInvoiceEmailCommandHandler`: rejects a `Draft` invoice with no side effects; succeeds for `Unpaid`/`Overdue`/`Paid`/`Cancelled` without mutating invoice status; records a new `Notification` with a fresh `SourceEventId` each call
- `ResendEmailSender`: builds a correct JSON request with base64-encoded attachments when present (against a fake `HttpMessageHandler`, no real Resend call)
- `ChangePasswordCommandHandler`: resolves the caller via `ICurrentUserService` (never a client-supplied id), sets the new password without a current-password check, and clears `MustChangePassword` to `false` on success; `ChangePasswordCommandValidator` enforces the same shared `PasswordRules()` used by `RegisterCommandValidator`/`ResetPasswordCommandValidator`
- `GetBusinessProfileQueryHandler` / `UpdatePaymentDetailsCommandHandler`: both resolve the business via `ICurrentUserService.BusinessId`; `UpdatePaymentDetailsCommandHandler` writes the submitted value (including setting it back to `null`) with no role check performed
- `CreateInvoiceCommandHandler`: `PaymentDetails` is snapshotted from the business's current `Business.PaymentDetails` when the create request supplies no override; an explicit override in the request is used instead of the business default; the snapshotted/overridden value is unaffected by a later change to `Business.PaymentDetails`
- `UpdateInvoiceCommandHandler`: accepts a change to `Invoice.PaymentDetails` while the invoice is `Draft`; rejects it (along with the rest of the update) once the invoice is no longer `Draft`, identical to the existing line-items/due-date/client lock
- `QuestPdfInvoiceGenerator`: renders the "Payment Details" section when `Invoice.PaymentDetails` is non-empty; omits the section entirely (no empty heading) when it's null/empty
- `InvoiceEmailContentBuilder`: `Build(...)` and `BuildOverdueReminder(...)` each include the payment-details line when `Invoice.PaymentDetails` is non-empty and omit it entirely when null/empty

### Backend integration tests (`InvoiceTracker.IntegrationTests` — xUnit, `WebApplicationFactory`, Testcontainers for Postgres)

Full HTTP pipeline, real database, real migrations:
- Auth — register creates Business+Owner; login succeeds/fails correctly; forgot/reset-password round trip (with a fake `IEmailSender` registered for tests, never a real Resend call)
- Multi-tenancy isolation — Business A creates a client, an invoice, and (indirectly, via an overdue invoice) a notification; Business B's JWT requests each of the three → `404`, proving the BusinessId-scoping rule holds at the HTTP layer for all three resources, not just one
- Client & Invoice CRUD — including the Draft-only edit lock, and that removing a Client (`Status → Removed`) or archiving an Invoice (`ArchivedAt`) each hides the record from the default list without deleting it
- Invoice numbering concurrency — fire concurrent create-invoice requests for the same business, assert no duplicate numbers (proves the row-locking counter approach under contention)
- Role-based authorization — Staff attempting `POST /api/users` → `403`; Owner succeeds; Owner attempting to deactivate their own account → `400`; Owner reactivating a previously deactivated Staff user succeeds and the user can log in again
- Dashboard summary — seed invoices across statuses, assert the numbers match
- PDF export — response is a valid PDF (content-type + `%PDF` magic bytes), not a visual/content assertion
- `POST /api/invoices/{id}/send` response includes `EmailDelivery.Status`; invoice transitions to `Unpaid` even when the test's fake `IEmailSender` is configured to fail
- `POST /api/invoices/{id}/resend-email` → `400` for a `Draft` invoice, `200` + new `Notification` row for a non-`Draft` one
- `GET /api/notifications` list DTO includes `NotificationType` and distinguishes `OverdueReminder` from `InvoiceSent` rows
- Forced password change — `POST /api/auth/login` response's `MustChangePassword` is `true` immediately after a Staff user is created via `CreateStaffUser` (temp password), and `false` for a freshly registered Owner; `POST /api/auth/change-password` requires a valid JWT (`401` without one); end-to-end, a Staff user created via `CreateStaffUser` logs in with the temp password, calls `change-password` with just `{ newPassword }`, succeeds, and a subsequent login shows `MustChangePassword: false`
- Multi-business login — an identity with exactly one active membership logs in exactly as before (unchanged response shape); an identity with two active memberships (one Owner, one Staff, in different businesses) gets `requiresBusinessSelection: true` with both listed; `select-business` with a valid `preAuthToken` + a business the identity belongs to succeeds; `select-business` with a business id the identity does *not* belong to is rejected even with a structurally valid `preAuthToken`
- Switch-business — succeeds and returns a token scoped to the new business/role for a membership the caller actually holds; `404` for a business id the caller's identity has no active membership in; a JWT issued for Business A is rejected (`404`, the existing cross-tenant rule) when used to call any Business-B-scoped endpoint, proving a switch requires a new token rather than letting one token silently span businesses
- Register with an already-registered email — `409 Conflict`, no new Business/identity/membership created; "Add a business" (authenticated) for an existing identity succeeds and does not disturb its existing membership(s)
- Deactivating a membership in one business does not affect the same identity's membership (or ability to log into) a different business
- `GET /api/business` / `PUT /api/business/payment-details` — succeeds for both an Owner's and a Staff user's JWT (no `403` for either); Business A's JWT reading/updating Business B's payment details is unreachable (`404`, the same cross-tenant rule as every other resource) since the handler never takes a business id from the request; a value set via `PUT` is reflected by a subsequent `GET`
- Invoice creation snapshots `PaymentDetails` from the business's current default at the moment of creation; changing the business default afterward does not change an already-created invoice's `PaymentDetails`; supplying an explicit `paymentDetails` value on the create request takes precedence over the business default
- `PUT /api/invoices/{id}` can change `PaymentDetails` while the invoice is `Draft`; the same call is rejected once the invoice is `Unpaid`, consistent with the existing Draft-only edit lock
- `GET /api/invoices/{id}/pdf` and the Send/Resend/overdue-reminder email content each include the payment-details line/section when the invoice's `PaymentDetails` is non-empty, and omit it entirely when null/empty

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
11. **Design decisions & trade-offs** — the most important section: clean architecture + CQRS/MediatR, JWT+Identity over cookie auth, transactional outbox for Kafka publishing, KRaft mode Kafka, the Resend sandbox-mode delivery limitation, the `t3.micro` RAM trade-off and mitigations, why K8s manifests aren't live, Draft-only invoice editing, centralized status transitions (`TransitionTo`) applied consistently across Invoice/Client/User, soft-delete via `Status`/`ArchivedAt` over hard delete, per-business invoice numbering under concurrency, idempotent Kafka consumption via `SourceEventId`
12. Known limitations / possible future work — ties back to `requirements.md`'s out-of-scope list
13. Folder structure overview (brief, linking back to this document)

## Build order

A sequence that gets something runnable early and layers on from there:

1. **Repo & tooling scaffold** — solution + projects (Domain/Application/Infrastructure/Api/UnitTests/IntegrationTests), Vite+React+TS frontend scaffold, initial `docker-compose.yml` (Postgres + Kafka in KRaft mode)
2. **Domain + persistence foundation** — entities, enums, EF Core `DbContext` + configurations, first migration
3. **Auth** — Identity + JWT issuance, register/login, `ICurrentUserService` + BusinessId scoping, forgot/reset password (with a no-op `IEmailSender` for now)
4. **Core CRUD** — Clients and Invoices (incl. line items, per-business invoice numbering, status-transition endpoints, archive)
5. **Team management** — Owner-only Staff user creation/listing/deactivation/reactivation
6. **Kafka pipeline** — `OutboxMessage` + migration, scanner, relay, Kafka topic, consumer, `ResendEmailSender`, Notifications endpoint
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
