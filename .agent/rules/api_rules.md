# SLA & Penalty Settlement Engine - API Rules

- Giraffe handlers stay thin: validate input, call application services, map the result.
- Every protected route requires tenant context from API-key middleware.
- Use consistent domain error mapping; do not expose stack traces or upstream secrets.
- Mutating handlers write audit events.
- Breach ingestion endpoints enforce idempotency by `(tenant_id, source, source_ref)` where `source_ref` exists.
- `/api/breaches/from-hub` must verify HMAC before parsing tenant-scoped payloads.
