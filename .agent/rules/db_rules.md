# SLA & Penalty Settlement Engine - Database Rules

- Every data-bearing table includes `tenant_id` unless it is a global static catalog explicitly called out in the PRD.
- Use `numeric(20,4)` for decimal measurements and cents-backed integers for ledger money where specified.
- `penalty_ledger` is insert-only; reversals and adjustments are compensating rows.
- Settlement membership is represented by join tables; never mutate ledger rows to attach them to a settlement.
- Use parameterized SQL or Dapper.FSharp; never concatenate user input into SQL.
- Migrations live in `db/migrations/` and are applied by `db/Migrate/`.
