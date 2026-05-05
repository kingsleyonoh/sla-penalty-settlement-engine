# SLA & Penalty Settlement Engine - Jobs Rules

- Hangfire jobs wrap application services; business logic belongs in `Slapen.Application`.
- External side effects flow through the outbox and include idempotency keys.
- Jobs must be tenant-aware and safe to retry.
- Dead-letter conditions are observable and alertable.
- Contract Lifecycle NATS consumers use durable names and do not acknowledge before the breach is persisted or safely rejected.
