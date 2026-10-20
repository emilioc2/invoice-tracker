# Business Invoice Tracker

Multi-tenant invoice tracking system: React + TypeScript frontend, C#/.NET Core backend, PostgreSQL, Kafka.

## Source of truth

Before making any implementation decision, check:
- `source_documents/requirements.md` — product requirements, domain model, in/out of scope
- `source_documents/architecture.md` — technical architecture: backend layering, CQRS/MediatR pattern, API routes, Kafka event contract, frontend structure, test plan, README outline, build order

These two documents are authoritative. If something here conflicts with them, they win — update this file instead.

## Tech stack

- Backend: C# / .NET Core Web API, Clean Architecture (Domain/Application/Infrastructure/Api), CQRS via MediatR, FluentValidation
- Database: PostgreSQL via EF Core
- Messaging: Kafka (KRaft mode), transactional outbox pattern for publishing
- Auth: ASP.NET Core Identity + JWT bearer tokens
- Email: Formspree via `IEmailSender` abstraction
- PDF: QuestPDF
- Frontend: React + TypeScript (Vite), React Router, TanStack Query, React Hook Form + Zod, Tailwind CSS + shadcn/ui
- Testing: xUnit/FluentAssertions/NSubstitute + Testcontainers (backend), Vitest/React Testing Library/MSW (frontend)

## Key conventions

- **Multi-tenancy**: every query/command handler filters by `BusinessId`, resolved server-side from the JWT via `ICurrentUserService` — never trust a client-supplied business ID. Cross-tenant access returns `404`, not `403`.
- **CQRS**: one command/query class + one handler class per use case (see `architecture.md`'s folder layout). Controllers only build a command/query and call `_mediator.Send(...)` — never call application logic directly.
- **Validation**: a FluentValidation validator per command/query, run automatically via the `ValidationBehavior` MediatR pipeline — handlers assume input is already valid.
- **Soft delete**: use `ArchivedAt` (nullable timestamp) on Client and Invoice, never a hard delete. `null` = active.
- **Invoice edit lock**: `PUT /api/invoices/{id}` only works while `Status = Draft`. Once sent (`Unpaid`), line items/due date/client are immutable — further changes mean Cancel + recreate.
- **Invoice status workflow**: `Draft → Unpaid → Overdue → Paid`, `Draft/Unpaid → Cancelled` (not allowed once `Paid`).
- **Kafka reliability**: status changes that need to publish an event use the transactional outbox pattern (`OutboxMessage` table) — never publish to Kafka directly inside the same method that updates the database.
- **Idempotency (crash-safe, not just redelivery-safe)**: the Kafka consumer writes a `Notification` row as `Status = Pending` *before* calling `IEmailSender`, then updates it to `Sent`/`Failed` after. A `SourceEventId` match with `Status = Sent` is skipped; a match with `Status = Pending` is retried. Never check-then-send-then-record — that ordering has a crash window that causes real duplicate sends.
- **Per-business invoice numbering**: `INV-0001`, `INV-0002`, scoped per business, generated transactionally via a `BusinessInvoiceCounter` row under row-level locking — not a native Postgres sequence (impractical for tenants created at runtime).
- **Invoice dates**: `IssueDate` is never directly editable — it's `null` while `Draft` and gets set automatically (and becomes immutable) the moment the invoice is sent. `CreatedAtDate` is the separate, system-set audit timestamp from row creation.
- **User deactivation**: Owner can deactivate a Staff user (`IsActive = false`); an Owner can never deactivate their own account — there is exactly one Owner per business, so this is what prevents a business from losing all user-management access.

## Testing

- Backend unit tests: pure domain/application logic, mocked dependencies — no real database or Kafka.
- Backend integration tests: `WebApplicationFactory` + Testcontainers-provisioned real Postgres — no real Kafka (see `architecture.md`'s "Kafka testing" decision).
- Frontend tests: a handful of high-value cases (form validation, status badge, protected route, filter query params) — not exhaustive coverage.

## What's explicitly out of scope

See `requirements.md` for the full list — notably: real payment gateway integration, recurring invoices, multi-currency/i18n, a live Kubernetes cluster (manifests are artifacts only), and advanced reporting beyond the dashboard summary. Don't add these unless asked.

## Development workflow

Each step in the Build order below is one "sequence activity" and follows this loop — do not skip steps of this loop or bundle multiple build-order steps together:

1. **Route the work to the right subagent** — use `csharp-developer` for backend/.NET work, `react-developer` for frontend/React work. Don't implement backend or frontend code directly without going through the matching subagent.
2. **`invoice-qa-engineer` tests it** — after the dev subagent finishes a step, `invoice-qa-engineer` writes/runs the relevant tests from this document's "Testing" section and, where applicable (anything involving the running application — API endpoints, the Kafka pipeline, a UI screen), verifies it actually works in a local environment (`docker-compose up` + `dotnet test`/`npm test`/manual exercising) — not just that the code compiles.
3. **Commit** — once a step is implemented and verified by `invoice-qa-engineer`, the orchestrating session (not a subagent) commits the change with a message describing that step. Don't commit a step that hasn't been tested yet. **Subagents never run `git commit` (or `git push`) themselves, under any circumstances** — not `csharp-developer`/`react-developer` after implementing, and not `invoice-qa-engineer` after verifying. Committing is how the QA gate is enforced, so a subagent committing its own work bypasses that gate even if the work happens to be fine. A subagent that finishes a step hands back to the orchestrator; the orchestrator routes to QA, then commits.
4. **Push only when explicitly asked** — commits stay local until the project owner asks for a push.

## Build order

Follow the sequence in `architecture.md`'s "Build order" section — domain/persistence first, then auth, then core CRUD, then the Kafka pipeline, then frontend, then containerization/deployment/CI.
