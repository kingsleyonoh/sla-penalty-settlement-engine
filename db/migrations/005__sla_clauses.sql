create table sla_clauses (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    contract_id uuid not null references contracts(id),
    reference text not null,
    metric text not null,
    measurement_window text not null check (
        measurement_window in ('daily', 'weekly', 'monthly', 'quarterly', 'per_incident')
    ),
    target_value numeric(20,4) not null,
    penalty_type text not null check (
        penalty_type in (
            'flat_per_breach',
            'percent_of_monthly_fee',
            'tiered',
            'compounding_daily',
            'linear_per_unit_missed'
        )
    ),
    penalty_config jsonb not null,
    cap_per_period_cents bigint,
    cap_per_contract_cents bigint,
    accrual_start_from text not null default 'breach_observed_at' check (
        accrual_start_from in (
            'breach_observed_at',
            'breach_reported_at',
            'next_billing_period_start'
        )
    ),
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index idx_sla_clauses_tenant_contract_active
    on sla_clauses (tenant_id, contract_id, active);
