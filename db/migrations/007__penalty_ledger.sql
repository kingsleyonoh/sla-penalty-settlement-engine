create table penalty_ledger (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    sla_clause_id uuid not null references sla_clauses(id),
    breach_event_id uuid not null references breach_events(id),
    counterparty_id uuid not null references counterparties(id),
    contract_id uuid not null references contracts(id),
    entry_kind text not null check (entry_kind in ('accrual', 'reversal', 'adjustment')),
    direction text not null check (direction in ('credit_owed_to_us', 'mirror')),
    amount_cents bigint not null check (amount_cents > 0),
    currency char(3) not null,
    accrual_period_start timestamptz not null,
    accrual_period_end timestamptz not null,
    compensates_ledger_id uuid references penalty_ledger(id),
    reason_code text not null check (
        reason_code in (
            'sla_breach',
            'dispute_resolved_in_our_favor',
            'dispute_resolved_against',
            'operator_correction',
            'contract_cap_applied',
            'withdrawn_by_source'
        )
    ),
    reason_notes text,
    created_at timestamptz not null default now(),
    created_by_kind text not null check (created_by_kind in ('system', 'user', 'adapter')),
    created_by_user_id uuid references users(id),
    check (accrual_period_end >= accrual_period_start)
);

create index idx_penalty_ledger_tenant_counterparty_currency_created
    on penalty_ledger (tenant_id, counterparty_id, currency, created_at);

create index idx_penalty_ledger_breach_event
    on penalty_ledger (breach_event_id);
