create table counterparties (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    canonical_name text not null,
    normalized_name citext not null,
    tax_id text,
    country_code char(2),
    external_refs jsonb not null default '{}'::jsonb,
    default_currency char(3),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, normalized_name)
);

create index idx_counterparties_contract_lifecycle_ref
    on counterparties (tenant_id, (external_refs ->> 'contract_lifecycle'));
