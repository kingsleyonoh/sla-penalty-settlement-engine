create table contracts (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    counterparty_id uuid not null references counterparties(id),
    reference text not null,
    title text not null,
    source text not null check (source in ('manual', 'contract_lifecycle', 'csv_import')),
    external_ref text,
    currency char(3) not null,
    effective_date date not null,
    expiry_date date,
    status text not null check (status in ('active', 'expired', 'terminated')),
    document_url text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, reference)
);

create index idx_contracts_tenant_counterparty_status
    on contracts (tenant_id, counterparty_id, status);
