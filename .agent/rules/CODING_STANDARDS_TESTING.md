# SLA & Penalty Settlement Engine — Coding Standards: Testing (Core TDD)

> Part 2 of 4. Also loaded: `CODING_STANDARDS.md`, `CODING_STANDARDS_TESTING_LIVE.md`, `CODING_STANDARDS_DOMAIN.md`
> This file covers core TDD discipline. For mock policy, integration, component, and E2E testing → see `CODING_STANDARDS_TESTING_LIVE.md`.

## Testing Rules — Anti-Cheat (CRITICAL)

### Never Do These
- **NEVER modify a test to make it pass.** Fix the IMPLEMENTATION, not the test.
- **NEVER use `pass` or empty test bodies.**
- **NEVER hardcode return values** just to satisfy a test.
- **NEVER hardcode tenant-identity literals in templates/emails/invoices** just to make a template test pass. If `{{entity.X}}` doesn't resolve, extend the schema or escalate — never inline the literal. See `CODING_STANDARDS.md` — "No Silent Workarounds" and `CODING_STANDARDS_DOMAIN.md` — "Multi-Tenant Config-Driven Surfaces."
- **NEVER use broad exception handlers** to swallow errors that would make tests fail.
- **NEVER mock the thing being tested.** Only mock external dependencies.
- **NEVER skip or mark tests as expected failures** without explicit user approval.
- **NEVER weaken a test assertion** to make it pass.
- **NEVER delete a failing test.** Failing tests are bugs. Fix them.
- **NEVER run template/email/invoice tests against only one tenant fixture.** Single-tenant fixtures mask cross-tenant leakage. See "Multi-Tenant Fixtures Mandatory" below.

### TDD Sequence is Non-Negotiable
- Tests FIRST, then implementation. Never the reverse.
- You MUST create test files BEFORE creating implementation files.
- You MUST run tests and see RED (failures) before writing any implementation.
- You MUST show the RED PHASE EVIDENCE output (as defined in `implement-next.md` Step 5) before proceeding to Green Phase.
- The ONLY exception: `[SETUP]` items (scaffolding, config, infrastructure) where no testable behavior exists yet.
- If you catch yourself implementing without tests — STOP, delete the implementation, write the tests first.

### Always Do These
- **Test BEHAVIOR, not implementation.**
- **Test edge cases:** empty inputs, None, zero, negative, missing, duplicate.
- **Test sad paths:** API errors, timeouts, invalid data.
- **Assertions must be specific:** `assertEqual(result, expected)`, not `assertIsNotNone(result)`.

### Tests Run Real Code (Anti-Bypass — CRITICAL)
- **Tests MUST exercise the production code path.** No test-only seed scripts that bypass the real seeder. No test-only schema migrations that diverge from production. No test-only binary substitutes that approximate the real one. No in-memory replacements for services you control locally (per the mock policy in `CODING_STANDARDS_TESTING_LIVE.md`).
- **Fixtures derive from production data sources, not parallel definitions.** If production reads entity config from `config/entities.json` (or seeds from `scripts/seed.*`, or any canonical source), test fixtures derive from THAT source — they don't redefine entity shape independently.
- **Common bypasses to reject:** test fixture inserts rows directly into the DB, skipping the real upsert/seed pipeline; test runs against a different schema (older migration state, simplified mock schema); test uses a stub binary (e.g. fake PDF generator) where production uses a real one (gs / wkhtmltopdf / chromium); test uses `:memory:` SQLite where production uses Postgres; test loads templates via a helper that bypasses the real boot-time loader.
- **Why:** every bypass is a place where tests pass while production fails. Your test green tells you the bypass works, not the production path. The bug surfaces only when the bypassed code runs against real data — usually after deploy.
- **Project-specific bypass-blockers** (which seed file, which binary, which loader) belong in `.agent/knowledge/checks/` — `yolo-subagent-reinforce` writes them after a recurrence; you can also seed them manually. The principle here is universal; the enforcement specifics are project-local.

### Unhappy-Path Coverage Mandate (CRITICAL)
- **Every happy-path test MUST have at least one unhappy-path companion test of the same surface.** Surface = endpoint, function, command, render, job, page, consumer, whatever the project emits.
- **Companion shapes:** invalid input → 4xx; missing required field → validation error; auth boundary → 401/403; downstream failure → graceful error; empty/zero/negative input → defined behavior; concurrent / duplicate request → idempotent or rejected; resource-not-found → 404; over-limit input → 413/429.
- **The companion test must FAIL when the unhappy path is unhandled.** A test that passes when the system silently swallows the bad input is a false negative. Assert the specific error response, not just `expect(response).toBeDefined()`.
- **Why:** happy-path-only coverage misses the entire failure surface. Production rarely fails in the happy path — it fails when input is malformed, the network drops, the DB is locked, the user double-clicks. The bugs you ship are always in the unhappy paths you didn't test.
- **No fixed count threshold** — "1 unhappy path per happy path" is the floor, not a ceiling. Surfaces with multiple failure modes need multiple companions. Surfaces without meaningful unhappy paths (pure constants, type definitions, trivial getters) are exempt — but document the exemption in the test file's header so a reviewer can audit it.

### Strictest-Validation-Default (CONDITIONAL — applies when validation tiers exist)
- **When a feature has multiple validation strictness levels, tests default to the STRICTEST tier.** Examples: XML schema profiles (multiple compliance levels), JSON Schema strict-vs-lenient modes, parser strictness flags, ESLint severity tiers, PDF compliance levels (PDF/A-1 vs A-2 vs A-3), email RFC-strictness modes.
- **Lenient tiers are explicit secondary tests** with a documented justification — "test against tier-N because production uses tier-N for this surface." Without that documentation, default to strictest.
- **Why:** lenient-tier tests produce false GREEN. Code that passes the lenient validator can still fail the strict one — and production often runs strict (regulatory compliance, downstream consumer requirements, security profiles). When the tier flips, the latent failures all surface at once.
- **Skip this rule entirely when no tiers exist.** Most surfaces have one validation pass — the rule fires only when there are multiple.

## Test Quality Checklist (Anti-False-Confidence)

Before moving from RED → GREEN, verify ALL applicable categories have tests:

| # | Category | What to Test |
|---|----------|-------------|
| 1 | Happy path | Does it work with valid, normal input? |
| 2 | Required fields | Does it reject None/blank for required fields? |
| 3 | Uniqueness | Does it enforce unique constraints? |
| 4 | Defaults | Do default values apply correctly when field is omitted? |
| 5 | FK relationships | Do foreign keys enforce CASCADE/PROTECT correctly? |
| 6 | Tenant isolation | Can Tenant A see Tenant B's data? (MANDATORY if multi-tenant — see Multi-Tenant Fixtures Mandatory below; includes template / email / invoice / PDF rendering) |
| 7 | Edge cases | Empty strings, zero, negative, very long strings, special chars |
| 8 | Error paths | What happens when external APIs fail, DB is down, input is malformed? |
| 9 | String representation | Does `__str__` / `__repr__` return something meaningful? |
| 10 | Meta options | Are ordering, indexes, and constraints working? |

**If a category applies and you skip it, you're cheating.** If RED phase shows fewer than 2 failures, add more tests — you're probably not testing enough.

### Performance Awareness
- Correctness tests alone don't catch latency regressions — a page can pass all tests while making 10× the necessary network calls
- When a single page/endpoint triggers 3+ backend operations, consider asserting call count or response time
- After every batch of 5+ features, do a compound load check: load real pages and verify total I/O matches expectations

### Multi-Tenant Fixtures Mandatory (CRITICAL — Catches Cross-Tenant Leakage)

If the project is multi-tenant (PRD §2 Architecture Principles mandates `tenant_id`), every test suite that touches tenant-scoped data MUST load **at least TWO distinct tenants** with different literal values for every tenant-identity column (legal_name, full_legal_name, display_name, address, registration, contact, wordmark).

**Why:** A template that hardcodes "Acme Corp LLC" passes every test when the fixture only loads Acme. It fails the moment Globex is onboarded. Two-tenant fixtures expose this at RED phase, not in production.

**Rules:**

1. **Fixtures file (`tests/fixtures/tenants.*` or equivalent) MUST define ≥2 tenants** with intentionally-different identity values. Include edge cases: non-ASCII characters, longer addresses, different jurisdictions.
2. **Template / email / invoice / PDF tests MUST parametrize over both tenants** (pytest parametrize, table-driven tests, etc.) and assert that rendering Tenant A's snapshot does NOT include any Tenant B literal value and vice versa.
3. **Cross-tenant leakage grep (runs in suite):** Add a test that reads the generated artifact and greps for EVERY literal identity value of the OTHER tenant. Any match fails the test with message `TENANT_IDENTITY_LEAK: field=X expected=A actual_included=B`.
4. **Tenant isolation test per module:** Category 6 in the Test Quality Checklist above becomes MANDATORY (was conditional "if multi-tenant"). Every query, every API response, every job run must be asserted to respect `tenant_id` scoping.

**This rule is non-optional for config-driven surfaces.** Skipping it means the template-hardcoding bug class (a surface hardcodes one tenant's literal identity, tests pass under a single-tenant fixture, leaks to production when a second tenant onboards) will re-occur project-by-project until tests catch it at RED.

## Edge Case Coverage Guide

### Models
- Every field from the spec → at least 1 test per constraint
- Every FK → test CASCADE behavior
- Every choice field → test all valid values + 1 invalid value

### Services (when applicable)
- Boundary values (min, max, zero, negative)
- Invalid input types
- Idempotency (running twice = same result)
- Mock external API failures

### Views/Pages (when applicable)
- Authenticated vs unauthenticated access
- Correct HTTP methods (GET/POST/PUT/DELETE)
- Response format validation
- Tenant scoping (if multi-tenant)

## Test Modularity Rules
1. **One test class per model/service** — never mix models in one class
2. **Max 300 lines per test file** — split if larger
3. **`setUp` creates only what that class needs** — no global fixtures
4. **Tests are independent** — no shared state, no ordering dependency
5. **Any single test can run in isolation** — `python -m pytest tests/test_x.py::TestClass::test_method`
6. **Test names describe business behavior** — not technical actions
7. **No test helpers longer than 10 lines** — extract to a `tests/factories.py` if needed

## Business-Context Testing
- Tests must reflect the BUSINESS PURPOSE described in the spec.
- Every test must answer: Does this protect data? Apply rules correctly? Handle failure? Match the spec?
- Test names must describe business behavior, not technical actions.

> **Integration, Component, E2E, and Mock Policy rules** → see `CODING_STANDARDS_TESTING_LIVE.md` (Part 3 of 4).
