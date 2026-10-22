# Eternity Invoicing — Requirements Document

## Overview

Eternity Invoicing is a multi-tenant web application that allows businesses to manage clients and invoices, track payment status through a defined workflow, and automatically trigger notifications when invoices become overdue.

## Tech & infrastructure decisions

| Area | Decision |
|---|---|
| Backend | C# / .NET Core Web API, layered/clean architecture (Domain / Application / Infrastructure / API) |
| Auth | ASP.NET Core Identity (password hashing, lockout, email-confirmation/reset token plumbing) underneath, issuing JWT bearer tokens for the SPA to call the API cross-origin. Includes self-service "forgot password" flow (Identity reset token + email via `IEmailSender` + reset-password page). No refresh-token flow — an expired JWT simply forces re-login (the frontend intercepts `401` and redirects to `/login`). This stateless, no-refresh-token design also means there is no token-revocation mechanism: deactivating a user (see User entity below) sets `IsActive = false` immediately and blocks all future logins, but does not revoke any JWT already issued to that user — a token issued before deactivation remains valid and usable until it naturally expires at the end of its configured lifetime. This is an accepted limitation of the design, not a bug. |
| Database | PostgreSQL |
| Messaging | Kafka, **KRaft mode** (no separate Zookeeper container — lighter footprint) |
| Frontend | React + TypeScript |
| Email | Formspree, behind a generic `IEmailSender` interface (Application layer) so the concrete provider is a one-class swap later. **Known limitation to document:** Formspree forwards to a configured inbox rather than arbitrary third-party recipients, so in this implementation it proves the outbound integration fires correctly on events, rather than guaranteeing delivery to each client's actual inbox. |
| PDF export | QuestPDF for generating an invoice PDF on demand. (Its free Community license has company-size/revenue eligibility conditions — acceptable here as a personal project, called out for completeness alongside the other licensing/cost trade-offs in this table.) |
| Frontend hosting | Vercel (live), using Vercel's own git integration for frontend CI/CD |
| Backend/DB/Kafka hosting | Single AWS EC2 **t3.micro**, running the full `docker-compose` stack (API + Postgres + Kafka) live. 1GB RAM is tight for this combination — mitigations: KRaft mode Kafka, trimmed JVM heap settings, lightweight Postgres config, swap file configured on the instance. |
| CI | GitHub Actions: `dotnet build`/`test` on PR, build + push Docker image to GitHub Container Registry |
| Kubernetes | Deployment/Service/ConfigMap/Secret manifests written as artifacts demonstrating container orchestration, runnable against `kind`/`minikube` — not deployed to a live cluster |

## Domain model

- **Business** — the tenant. `Id, Name, Email`
- **User** — `Id, BusinessId, Email, PasswordHash (via Identity), Role (Owner | Staff), IsActive`. `IsActive` defaults to `true`; an Owner can set a Staff user's to `false` (deactivation), which blocks future logins immediately but — per the Auth row above — does not revoke a JWT already issued to that user; it remains valid until it expires. An Owner can never deactivate their own account — see Permissions below. **Note:** the `Owner` role is displayed to end users in the UI as "Admin" (a display-only label change). The internal role identifier, the `UserRole.Owner` enum value, and the persisted `"Owner"` string in the database are unchanged — every other mention of "Owner" in this document refers to this unchanged internal value.
- **Client** — the business's customer being invoiced. `Id, BusinessId, Name, Email, Phone, Address, CreatedAtDate, LastModifiedDate, ArchivedAt`. `ArchivedAt` is a nullable timestamp: `null` means active, a timestamp means archived (and records when) — replaces a separate boolean/delete-timestamp pair.
- **Invoice** — `Id, BusinessId, ClientId, InvoiceNumber, CreatedAtDate, IssueDate (nullable), DueDate, Status, Subtotal, TaxRate, TaxAmount, Total, Notes, LastModifiedDate, ArchivedAt`. `InvoiceNumber` is auto-incrementing *per business* (e.g. `INV-0001`, `INV-0002`, each business starts its own sequence) — needs a per-business counter generated transactionally: a `BusinessInvoiceCounter` row (one per business) incremented under row-level locking within the same transaction as invoice creation, to avoid race conditions when multiple Staff users create invoices concurrently. (A native Postgres sequence per business was considered and rejected: it would mean dynamically running `CREATE SEQUENCE` on every business registration, which doesn't fit a system where tenants are created at runtime.) Tax is calculated at the invoice level (single `TaxRate`/`TaxAmount` against the subtotal), not per line item. `ArchivedAt` follows the same nullable-timestamp pattern as Client. `CreatedAtDate` is set at row creation (while still `Draft`) for audit purposes. `IssueDate` is `null` while `Draft` and is never directly editable by the business — it's set automatically, and becomes immutable, the moment the invoice is sent (`Draft → Unpaid`); `DueDate` remains editable like other fields only while still `Draft`, per the existing edit-lock rule.
- **InvoiceLineItem** — `Id, InvoiceId, Description, Quantity, UnitPrice, LineTotal`
- **Notification** — record of a simulated overdue notice. `Id, InvoiceId, Status (Pending | Sent | Failed), SentAt (nullable), Channel, Message, SourceEventId`. `SourceEventId` (unique) ties the notification back to the Kafka event that triggered it. The row is inserted as `Pending` *before* the email send is attempted, not after — so a crash between sending and recording never results in the same event being processed twice. See `architecture.md`'s consumer design for the full sequence.

### Status workflow
`Draft → Unpaid` (manual, business finalizes/sends — this is also the moment `IssueDate` is set) → `Unpaid → Overdue` (automatic, background job once `DueDate` passes unpaid) → `Unpaid/Overdue → Paid` (manual) → `Draft/Unpaid → Cancelled` (manual; not allowed once Paid).

### Permissions
- **Owner**: full CRUD on clients/invoices + can create and deactivate other Users for the business. Exactly one Owner exists per business (the user who registered it) — Owners cannot create additional Owners, and **an Owner can never deactivate their own account**, so a business can never end up with no one able to manage users.
- **Staff**: full CRUD on clients/invoices, no user management

### Multi-user onboarding
First registration creates the Business + its Owner user. Owner adds Staff users directly from the UI (email + auto-generated temp password, emailed via the `IEmailSender` abstraction) — no separate invite-token flow.

## In scope

- Business + Owner registration/login; Owner-created and Owner-deactivated Staff users (an Owner can never deactivate their own account); JWT auth; self-service forgot-password/reset flow
- Client CRUD (archive via `ArchivedAt`, not hard delete)
- Invoice CRUD with line items (archive via `ArchivedAt`, not hard delete), full status workflow above
- Kafka-driven overdue pipeline: background job flags overdue invoices → publishes event → consumer "sends" via Formspree + persists a `Notification`
- Notifications list view
- Invoice list with filtering (status, client, date range) + dashboard summary (outstanding total, overdue count, etc.)
- PDF export of an invoice
- Swagger/OpenAPI docs
- Full `docker-compose` stack (API, Postgres, Kafka in KRaft mode) — this is both the local dev environment and what's deployed live to the EC2 instance
- Live deployment: frontend on Vercel, backend stack on the AWS EC2 instance
- Kubernetes manifests as artifacts (not live)
- GitHub Actions CI for backend; Vercel's own pipeline for frontend
- Automated tests: backend unit tests (status transitions, overdue calculation, totals), backend integration tests (API against a real Postgres), a handful of frontend component tests
- README: run/build/test instructions, architecture overview, and a design decisions & trade-offs section (Formspree limitation, AWS t3.micro RAM trade-off, why K8s manifests aren't live, etc.)

## Out of scope

- Real payment gateway integration (`Paid` is a manual status flip)
- Recurring invoices / invoice templates
- Multi-currency, i18n
- Actual live Kubernetes cluster deployment (manifests only)
- Advanced reporting/analytics beyond the basic dashboard summary
