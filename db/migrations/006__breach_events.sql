create table breach_events (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    contract_id uuid not null references contracts(id),
    sla_clause_id uuid not null references sla_clauses(id),
    source text not null check (
        source in (
            'manual',
            'contract_lifecycle_nats',
            'contract_lifecycle_rest',
            'csv_import',
            'hub_ingress'
        )
    ),
    source_ref text,
    metric_value numeric(20,4) not null,
    units_missed numeric(20,4),
    observed_at timestamptz not null,
    reported_at timestamptz not null,
    raw_payload jsonb not null,
    status text not null check (
        status in ('pending', 'accrued', 'disputed', 'withdrawn', 'superseded')
    ),
    disputed_reason text,
    disputed_at timestamptz,
    disputed_by_user_id uuid references users(id),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index ux_breach_events_tenant_source_ref
    on breach_events (tenant_id, source, source_ref)
    where source_ref is not null;

create index idx_breach_events_tenant_contract_status_observed
    on breach_events (tenant_id, contract_id, status, observed_at);

create index idx_breach_events_tenant_status_observed
    on breach_events (tenant_id, status, observed_at);
