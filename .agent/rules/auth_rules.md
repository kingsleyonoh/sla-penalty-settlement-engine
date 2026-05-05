# SLA & Penalty Settlement Engine - Auth Rules

- API authentication uses `X-API-Key`; never accept tenant identity from request body or query params.
- API keys are stored as SHA-256 hashes with a 12-character prefix for lookup.
- Middleware resolves the tenant once and stores it in request context for downstream handlers.
- Cross-tenant access returns 404, not 403, when the resource exists for another tenant.
- UI authentication uses ASP.NET Core Identity scoped to tenant and cookie auth.
- API key rotation creates a new hash/prefix and audit event; raw keys are shown once only.
