create table settlement_ledger_entries (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    settlement_id uuid not null references settlements(id),
    ledger_entry_id uuid not null references penalty_ledger(id),
    role text not null check (role in ('included_accrual', 'included_reversal')),
    released_at timestamptz,
    created_at timestamptz not null default now(),
    unique (tenant_id, settlement_id, ledger_entry_id)
);

create unique index ux_settlement_ledger_entries_active_ledger
    on settlement_ledger_entries (tenant_id, ledger_entry_id)
    where released_at is null;

create index idx_settlement_ledger_entries_tenant_settlement
    on settlement_ledger_entries (tenant_id, settlement_id);

create index idx_settlement_ledger_entries_tenant_ledger
    on settlement_ledger_entries (tenant_id, ledger_entry_id);
