# SLA & Penalty Settlement Engine - Codebase Context

> Last updated: 2026-05-05
> PRD: `docs/sla-penalty-settlement-engine_prd.md`

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language | F# 8 on .NET 8 |
| Framework | Giraffe 6.x + ASP.NET Core 8 |
| Database | PostgreSQL 16 with `citext`, `numeric(20,4)`, and `tstzrange` |
| Data Access | Dapper.FSharp + raw SQL for reports |
| Cache / Queue | Redis 7 and Hangfire 1.8 with PostgreSQL backing |
| HTTP Client | `IHttpClientFactory` + Polly |
| Validation | FsToolkit.ErrorHandling + domain `Result<'T, DomainError>` |
| Serialization | System.Text.Json + FSharp.SystemTextJson |
| Auth | API key middleware for API, ASP.NET Core Identity + cookies for UI |
| UI | Razor Pages + HTMX + Tailwind CSS |
| PDF | QuestPDF 2024.x |
| Tests | xUnit.NET, FsUnit, Verify.Xunit, Testcontainers, Playwright .NET |
| Observability | Serilog JSON, Sentry.NET, prometheus-net, Axiom |
| Hosting | Docker on Hetzner VPS at `slapen.kingsleyonoh.com` |
| Package Manager | Paket preferred, NuGet via `dotnet tool restore` acceptable |
| Formatting / Lint | Fantomas and FSharpLint |

## Project Structure

```text
sla-penalty-settlement-engine/
├── db/
│   ├── migrations/
│   └── Migrate/
├── src/
│   ├── Slapen.Domain/
│   ├── Slapen.Data/
│   ├── Slapen.Tenants/
│   ├── Slapen.Audit/
│   ├── Slapen.Templates/
│   ├── Slapen.Application/
│   ├── Slapen.Ecosystem/
│   ├── Slapen.Jobs/
│   ├── Slapen.Api/
│   └── Slapen.Ui/
└── tests/
    ├── Slapen.Domain.Tests/
    ├── Slapen.Data.Tests/
    ├── Slapen.Application.Tests/
    ├── Slapen.Api.Tests/
    ├── Slapen.E2E.Tests/
    └── Slapen.Ui.Tests/
```

## Key Modules

Modules live in `.agent/knowledge/modules/` as one file per module. Expected first module files:
- Domain and penalty rules: `src/Slapen.Domain/`
- Data repositories and migrations: `src/Slapen.Data/`, `db/migrations/`
- Tenant identity snapshots: `src/Slapen.Tenants/`
- Audit recorder: `src/Slapen.Audit/`
- Accrual, reversal, settlement, ingestion, and outbox orchestration: `src/Slapen.Application/`
- Ecosystem clients and NATS boundaries: `src/Slapen.Ecosystem/`
- Hangfire jobs: `src/Slapen.Jobs/`
- Giraffe HTTP handlers and middleware: `src/Slapen.Api/`
- Razor/HTMX ops console: `src/Slapen.Ui/`

## Database Schema

| Table | Purpose | Key Fields |
|-------|---------|-----------|
| `tenants` | Tenant identity, API key hash/prefix, branding, locale, currency | `id`, `slug`, `api_key_hash`, `default_currency` |
| `users` | Ops console users scoped to tenant | `tenant_id`, `email`, `role` |
| `counterparties` | Suppliers/vendors | `tenant_id`, `normalized_name`, `external_refs` |
| `contracts` | Contract projections and manual contracts | `tenant_id`, `counterparty_id`, `reference`, `source` |
| `sla_clauses` | Penalty rule storage | `metric`, `measurement_window`, `penalty_type`, `penalty_config` |
| `breach_events` | Raw observations and dispute state | `source`, `source_ref`, `status`, `observed_at` |
| `penalty_ledger` | Immutable append-only bilateral ledger | `entry_kind`, `direction`, `amount_cents`, `compensates_ledger_id` |
| `settlements` | Credit-note posting plans | `status`, `amount_cents`, `currency` |
| `settlement_ledger_entries` | Settlement membership without mutating ledger rows | `settlement_id`, `penalty_ledger_id` |
| `outbox` | Durable side-effect queue | `event_type`, `status`, `attempts`, `idempotency_key` |
| `ingestion_runs` | Adapter run tracking | `source`, `status`, `started_at`, `completed_at` |
| `audit_log` | Mutating action audit trail | `actor`, `action`, `resource_type`, `resource_id` |
| `signal_definitions` | VPI/notification signal catalog | `code`, `description`, `severity` |

## External Integrations

| Service | Purpose | Auth Method |
|---------|---------|------------|
| Contract Lifecycle Engine | Emits breach events via NATS and supports REST backfill | API key / NATS credentials |
| Invoice Reconciliation Engine | Receives credit notes and debit memos | API key + Idempotency-Key |
| Notification Hub | Sends settlement/dispute notifications | API key |
| Workflow Engine | Starts dispute escalation playbooks | API key |
| VPI | Receives supplier penalty signals | API key |
| Axiom / Sentry / PostHog / BetterStack | Logs, errors, analytics, uptime | Env-var credentials |

## Environment Variables

Documented in `.env.example`. Core variables include `DATABASE_URL`, `REDIS_URL`, `SESSION_SECRET`, `API_KEY_PREFIX`, `NOTIFICATION_HUB_*`, `WORKFLOW_ENGINE_*`, `CONTRACT_LIFECYCLE_*`, `NATS_*`, `INVOICE_RECON_*`, `VPI_*`, `HUB_INGRESS_SECRET`, `SENTRY_DSN`, `AXIOM_*`, `POSTHOG_*`, and metrics credentials.

## Commands

| Action | Command |
|--------|---------|
| Install dependencies | `dotnet restore Slapen.sln` |
| Dev server | `dotnet watch --project src/Slapen.Api` |
| Run tests | `dotnet test` |
| Run tests (unit only) | `dotnet test tests/Slapen.Domain.Tests tests/Slapen.Application.Tests` |
| Run tests (integration only) | `dotnet test tests/Slapen.Data.Tests tests/Slapen.Api.Tests` |
| Lint/check | `dotnet fantomas . --check && dotnet fsharplint lint Slapen.sln` |
| Build | `dotnet build Slapen.sln` |
| Migrate DB | `dotnet run --project db/Migrate` |
| E2E tests | `dotnet test tests/Slapen.E2E.Tests tests/Slapen.Ui.Tests` |
| Start infra | `docker compose up -d postgres redis` |
| Stop infra | `docker compose down` |
| Check infra | `docker compose ps` |

## Tenant Model

API requests use `X-API-Key`. Middleware hashes the key, matches the prefix/hash against `tenants`, and stores the resolved tenant on the request context. All repository methods require tenant scope, and cross-tenant misses return 404 instead of disclosing existence.

## Key Patterns & Conventions

Patterns live in `.agent/knowledge/patterns/`; use this file as the routing index, not as an append-only pattern log.

Required starting conventions:
- Domain core is pure F# and has no infrastructure dependencies.
- Ledger writes happen through a single domain transaction function that inserts paired bilateral rows together.
- Corrections and disputes write compensating entries; ledger rows are not updated or deleted.
- Outbound side effects write outbox entries first, then workers perform retries with idempotency keys.
- Tenant snapshots are captured at render/post time and reused for immutable PDF/legal artifacts.

## Gotchas & Lessons Learned

Gotchas live in `.agent/knowledge/gotchas/` as one file per gotcha.

## Shared Foundation (MUST READ before any implementation)

Foundation primitives live in `.agent/knowledge/foundation/`. Before coding against a surface, read the matching foundation file in full. Seed the first real foundation files when Phase 0 creates the solution, domain types, ledger writer, tenant snapshotter, and outbox.

## Deep References

| Topic | Where to look |
|-------|--------------|
| Domain model and penalty types | `src/Slapen.Domain/` |
| Database migrations | `db/migrations/` |
| Repository layer | `src/Slapen.Data/` |
| Tenant identity snapshots | `src/Slapen.Tenants/` |
| Ledger/accrual/reversal/settlement orchestration | `src/Slapen.Application/` |
| Ecosystem adapters | `src/Slapen.Ecosystem/` |
| Hangfire jobs | `src/Slapen.Jobs/` |
| HTTP API | `src/Slapen.Api/` |
| Ops console | `src/Slapen.Ui/` |
| Tests | `tests/` |
