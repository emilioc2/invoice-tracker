# Eternity Invoicing

A multi-tenant invoice tracking system: businesses manage clients and invoices, track payment status through a defined workflow, and automatically notify clients by email when an invoice becomes overdue.

## Overview

Eternity Invoicing lets a business register, add clients, create and send invoices, and track them through `Draft → Unpaid → Overdue → Paid` (or `Cancelled`). A background pipeline detects overdue invoices and emails the client a reminder automatically; invoices themselves can be downloaded as a PDF and emailed directly from the app. Multiple people can belong to the same business (Owner/Staff roles), and the same person's login can belong to more than one business at once.

## Tech stack

| Layer | Choice |
|---|---|
| Backend | C# / .NET 10 Web API, Clean Architecture (Domain / Application / Infrastructure / Api), CQRS via MediatR, FluentValidation |
| Database | PostgreSQL via EF Core |
| Messaging | Kafka (KRaft mode, no Zookeeper), transactional outbox pattern for publishing |
| Auth | ASP.NET Core Identity + JWT bearer tokens |
| Email | [Resend](https://resend.com), behind an `IEmailSender` abstraction |
| PDF | QuestPDF |
| Frontend | React + TypeScript (Vite), React Router, TanStack Query, React Hook Form + Zod, Tailwind CSS + shadcn/ui |
| Backend tests | xUnit, FluentAssertions, NSubstitute, Testcontainers |
| Frontend tests | Vitest, React Testing Library, MSW |
| Containerization | Docker, docker-compose |
| Backend hosting | AWS EC2 (`t3.micro`), via docker-compose |
| Frontend hosting | Vercel |
| CI/CD | GitHub Actions (backend build/test/image push to GHCR), Vercel's own git integration (frontend) |

See [Design decisions & trade-offs](#design-decisions--trade-offs) below for the reasoning behind these choices, and `source_documents/architecture.md` for full technical depth.

## Architecture overview

```
React SPA (Vercel)  --HTTPS-->  .NET API (EC2, behind Caddy)  --->  PostgreSQL
                                        |
                                        +--> Kafka (overdue-invoice events)
                                        |          |
                                        |          v
                                        +--> OverdueNotificationConsumer --> Resend (email)
```

The API is a layered Clean Architecture project (`Domain` → `Application` → `Infrastructure` → `Api`), with each use case implemented as a single MediatR command/query + handler. Invoice status changes that need to notify a client go through a transactional outbox (`OutboxMessage` table) rather than publishing to Kafka directly inside the same transaction, so a Kafka hiccup can never lose or duplicate an overdue-reminder email. See `source_documents/architecture.md` for the full design, including the domain model, Kafka event contract, and every request-handling pattern.

## Live deployment

- **Frontend:** https://invoice-tracker-frontend-sigma.vercel.app
- **Backend API:** https://13-53-216-75.nip.io (an EC2 instance's public IP via a free [nip.io](https://nip.io) hostname + Let's Encrypt cert — see [Deployment](#deployment) below for why)
- Swagger/OpenAPI is **not** exposed on the live deployment (see [API documentation](#api-documentation)) — run the API locally to browse it interactively.
- Kubernetes manifests exist as artifacts only (`k8s/`) — there is no live cluster; see [Design decisions & trade-offs](#design-decisions--trade-offs).

## Local development

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker (for the full `docker-compose` stack, and for backend integration tests, which use Testcontainers)

### Option A — full stack via Docker (simplest)

```bash
docker compose up -d
```

This builds and runs Postgres, Kafka, the API, and the frontend together. Once it's up:

| Service | URL |
|---|---|
| Frontend | http://localhost:5173 |
| API | http://localhost:5100 |
| Postgres | localhost:5432 (`invoicetracker` / `invoicetracker` / db `invoicetracker`) |
| Kafka | localhost:9092 (host tools), `kafka:29092` (in-network) |

The database schema is **not** auto-applied on container startup (see [Database migrations](#database-migrations)) — after the first `docker compose up`, apply migrations once:

```bash
cd backend/src/InvoiceTracker.Api
dotnet ef database update
```

### Option B — native dev loop (hot reload, faster iteration)

Bring up just Postgres + Kafka via Docker, then run the API and frontend natively:

```bash
docker compose up -d postgres kafka
cd backend/src/InvoiceTracker.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --launch-profile http
dotnet ef database update   # first time only, or after a new migration
```

```bash
cd frontend
cp .env.example .env        # VITE_API_BASE_URL defaults to http://localhost:5100
npm install
npm run dev                 # http://localhost:5173
```

The API's CORS policy (`Cors:AllowedOrigins` in `appsettings.Development.json`) already allows `http://localhost:5173` by default.

## Running tests

```bash
# Backend — unit tests (fast, no external dependencies)
cd backend
dotnet test tests/InvoiceTracker.UnitTests

# Backend — integration tests (needs Docker running: spins up a real Postgres via Testcontainers)
dotnet test tests/InvoiceTracker.IntegrationTests
```

```bash
# Frontend
cd frontend
npm test
```

## API documentation

Swagger/OpenAPI UI is wired up (`app.UseSwaggerUI()`) but only enabled when `ASPNETCORE_ENVIRONMENT=Development` — run the API locally (see [Local development](#local-development)) and open **http://localhost:5100/swagger**. It is intentionally not exposed on the live Production deployment.

## Database migrations

Migrations are **applied manually**, not automatically on container/application startup — there is no `Database.Migrate()` call in `Program.cs`. After bringing up a fresh Postgres (via either local-dev option above, or a fresh deployment), apply the schema with:

```bash
cd backend/src/InvoiceTracker.Api
dotnet ef database update
```

For a deployment target where the .NET SDK isn't installed (e.g. the live EC2 instance, which only runs the published container image), generate an idempotent SQL script locally instead and apply it directly against Postgres:

```bash
dotnet ef migrations script --idempotent --project ../InvoiceTracker.Infrastructure --output migrate.sql
# then apply migrate.sql against the target Postgres, e.g. via `psql -f` or `docker exec -i <postgres container> psql ... -f migrate.sql`
```

## Deployment

**Frontend** deploys via Vercel's own git integration — every push to `main` triggers a new production build automatically (root directory `frontend/`, framework Vite). `VITE_API_BASE_URL` is a Vercel project environment variable pointing at the live API.

**Backend** builds via GitHub Actions (`.github/workflows/backend-ci.yml`): every push to `main` that touches `backend/**` runs the full test suite, then builds and pushes `ghcr.io/emilioc2/invoice-tracker-api:latest` (tagged with both `latest` and the commit SHA — image pushes are gated to pushes on `main`, never PRs, since a PR's `GITHUB_TOKEN` has no `packages:write` scope). The image is then pulled and run on a single AWS EC2 `t3.micro` via `docker-compose.prod.yml` (Postgres + Kafka + the API), fronted by a Caddy reverse proxy that terminates HTTPS. Rolling out a new image on the instance today is a manual `docker compose pull && docker compose up -d` — there is no auto-deploy webhook from GHCR to the EC2 instance.

Caddy exists specifically because the live frontend is HTTPS (Vercel) and browsers block a secure page from calling a plain-HTTP API as "mixed content" — this was discovered by actually driving a real browser session against the live deployment, not assumed up front. Since the project has no purchased domain, Caddy gets a free Let's Encrypt certificate against a [nip.io](https://nip.io) hostname, which resolves straight back to the instance's own public IP (`13-53-216-75.nip.io` → `13.53.216.75`) — no DNS registration needed.

**Kubernetes manifests** (`k8s/`) exist as artifacts demonstrating Deployment/Service/ConfigMap/Secret orchestration for the API, Postgres, and Kafka — written to be runnable against a local `kind`/`minikube` cluster (and verified against a real `kind` cluster during development), but never deployed to a live cluster. See [Design decisions & trade-offs](#design-decisions--trade-offs) for why.

## CI/CD

`.github/workflows/backend-ci.yml` runs on every pull request and push to `main` that touches `backend/**`:

1. **`build-and-test`** — `dotnet restore`/`build`/`test` (both the unit and integration suites) against `InvoiceTracker.sln`, on every PR and push.
2. **`build-and-push-image`** — builds `backend/Dockerfile` and pushes to GHCR, only on a push to `main` (never a PR).

The frontend has no custom GitHub Actions workflow — Vercel's own git integration handles its build/test/deploy pipeline independently.

## Design decisions & trade-offs

- **Clean Architecture + CQRS/MediatR** — every use case is one command/query + one handler, keeping controllers as thin dispatchers. This adds a bit of ceremony for simple CRUD but keeps business logic out of controllers and easy to unit-test in isolation.
- **JWT + Identity over cookie auth** — the SPA and API are on different origins (Vercel vs. EC2/nip.io), so a bearer token is simpler than fighting cross-origin cookies. Trade-off: no refresh-token flow or server-side revocation — an expired token just forces re-login, and deactivating a user blocks *future* logins but doesn't invalidate a token already issued. Accepted as a reasonable trade-off for a project this size.
- **Transactional outbox for Kafka publishing** — an invoice going overdue and the resulting Kafka event are written in the same database transaction (via the `OutboxMessage` table), with a separate relay service doing the actual publish. This means the "invoice is overdue" fact is never lost even if Kafka is briefly unavailable, at the cost of a small publish delay.
- **KRaft-mode Kafka** — no separate Zookeeper container, which matters on a RAM-constrained single VM.
- **Resend sandbox-mode limitation** — the deployed `IEmailSender` implementation uses Resend's shared sender address with no domain verification, which in sandbox mode can only deliver to the email address that owns the Resend account, not arbitrary client inboxes. This is enough to prove the outbound integration (including PDF attachments) fires correctly on every triggering event, but not to guarantee delivery to a real client's inbox — verifying a real sending domain with Resend would lift this limitation.
- **AWS `t3.micro` RAM trade-off** — running Postgres + Kafka + the API together on a 1GB-RAM instance is tight. Mitigations actually applied: KRaft-mode Kafka (no Zookeeper), a trimmed Kafka JVM heap (`KAFKA_HEAP_OPTS=-Xmx384M -Xms384M` — the image's 1GB default heap alone exceeded the instance's total RAM), and a 2GB swap file on the instance. Kafka OOM'd on first boot before these were applied, confirming the trade-off is real, not theoretical.
- **Why the Kubernetes manifests aren't live** — they exist purely to demonstrate container-orchestration literacy (Deployment/Service/ConfigMap/Secret for each of the three components), not because the project needs a cluster at this scale; a single EC2 instance running docker-compose is simpler and cheaper for one small app. The manifests were verified against a real local `kind` cluster, which caught three genuine Kubernetes-specific bugs in the process (see git history around the Kubernetes-manifests commit) that a Docker-only comparison never would have surfaced.
- **Draft-only invoice editing** — line items, due date, and client can only change while an invoice is `Draft`; once sent, the only path forward is Cancel + recreate. This keeps the "what the client was actually sent" question unambiguous, at the cost of not supporting in-place corrections to a sent invoice.
- **Centralized status transitions (`TransitionTo`)** — `Invoice`, `Client`, and `UserBusinessMembership` each guard their own status changes through a single domain method rather than letting every caller re-implement "is this transition legal," so the transition rules live in exactly one place per entity.
- **Soft delete via `Status`/`ArchivedAt`, never a hard delete** — a removed client or archived invoice simply stops appearing in default list views; the row (and its history) is never destroyed.
- **Per-business invoice numbering under concurrency** — `INV-0001`, `INV-0002`, etc., scoped per business, generated via a row-locked `BusinessInvoiceCounter` rather than a native Postgres sequence (which would mean dynamically running `CREATE SEQUENCE` for every business created at runtime).
- **Idempotent Kafka consumption via `SourceEventId`** — the overdue-reminder consumer records a `Pending` `Notification` row *before* attempting to send, keyed by the triggering event's id, so a crash between sending and recording can never result in a duplicate send or a silently lost attempt.

## Known limitations / possible future work

Ties back to `source_documents/requirements.md`'s explicit out-of-scope list:

- No real payment gateway integration — `Paid` is a manual status flip, not a captured payment.
- No recurring invoices or invoice templates.
- No multi-currency support or i18n (amounts are formatted as ZAR).
- No live Kubernetes cluster (manifests only — see above).
- No reporting/analytics beyond the basic dashboard summary (outstanding total, overdue count, status breakdown).
- Swagger/OpenAPI is only reachable locally, not on the live deployment (see [API documentation](#api-documentation)).
- Rolling out a new backend image to the live EC2 instance is a manual step today, not an automated deploy hook.
- The EC2 instance's public IP (and therefore the `nip.io` hostname it's built on) isn't static — if the instance is ever stopped/restarted without an Elastic IP attached, the live API address would change and both Vercel's env var and Caddy's hostname would need updating.

## Folder structure

```
.
├── backend/
│   ├── src/
│   │   ├── InvoiceTracker.Domain          # Entities, enums, domain invariants
│   │   ├── InvoiceTracker.Application     # MediatR commands/queries/handlers, validators
│   │   ├── InvoiceTracker.Infrastructure  # EF Core, Identity, Kafka, email/PDF providers
│   │   └── InvoiceTracker.Api             # Controllers, DI composition, Program.cs
│   └── tests/
│       ├── InvoiceTracker.UnitTests
│       └── InvoiceTracker.IntegrationTests
├── frontend/
│   └── src/                               # React SPA (routes, components, api client, hooks)
├── k8s/                                   # Kubernetes manifests (artifacts only — see above)
├── docker-compose.yml                     # Local dev / full stack
├── docker-compose.prod.yml                # Live-deployment variant (EC2)
├── Caddyfile                              # Live-deployment HTTPS reverse proxy
└── .github/workflows/backend-ci.yml       # Backend CI/CD
```

See `source_documents/requirements.md` and `source_documents/architecture.md` for the full product/domain requirements and technical design this README summarizes.
