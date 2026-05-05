# Observability and Readiness

## What it establishes

Production observability is configured at API startup and readiness is exposed through explicit dependency boundary checks.

## Files

- `src/Slapen.Api/Program.fs` - Serilog JSON stdout, Sentry, prometheus-net, metrics endpoint wiring
- `src/Slapen.Api/Handlers/Health.fs` - `/api/health`, `/api/health/db`, `/api/health/ready`
- `src/Slapen.Api/Middleware/MetricsAuth.fs` - Basic auth for `/metrics`
- `src/Slapen.Data/ReadinessRepository.fs` - DB, outbox, and adapter readiness queries

## When to read this

Before changing logging, Sentry, metrics, health endpoints, readiness checks, or deploy monitoring.

## Contract

- `SENTRY_DSN` may be absent locally; use an empty DSN to disable Sentry without failing startup.
- `/metrics` is enabled unless `PROMETHEUS_ENABLED=false` and requires Basic auth when exposed.
- `/api/health/ready` fails closed when DB migrations, Redis, outbox, job boundary config, or enabled adapter health is not acceptable.
- Do not log secrets or credential values.

## Cross-references

- PRD section 10b
- `.env.example`
- `.env.production.example`
