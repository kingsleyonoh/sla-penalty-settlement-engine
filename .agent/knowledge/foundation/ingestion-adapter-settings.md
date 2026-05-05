# Ingestion Adapter Settings

## What it establishes

Ingestion adapter controls are tenant-scoped persisted settings with local no-op behavior while disabled.

## Files

- `db/migrations/018__ingestion_adapter_settings.sql`
- `src/Slapen.Data/IngestionSettingsRepository.fs`
- `src/Slapen.Application/IngestionControl.fs`
- `src/Slapen.Api/Ui/IngestionSettingsPages.fs`

## When to read this

Before changing ingestion adapter enablement, operator settings UI, readiness checks that include adapters, or pull-now/test behavior.

## Contract

- Every setting row is keyed by `(tenant_id, adapter)`.
- Unknown or disabled adapters must not trigger external side effects.
- `test` records `disabled` for disabled adapters and `healthy` only for enabled adapters in the local control path.
- `pull-now` records intent only when the adapter is enabled.

## Cross-references

- PRD Phase 3 ingestion settings item
- `tests/Slapen.Application.Tests/Batch011OperationalTests.fs`
- `tests/Slapen.Api.Tests/ApiIntegrationTests.fs`
