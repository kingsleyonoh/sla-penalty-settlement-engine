create table settlements (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    counterparty_id uuid not null references counterparties(id),
    contract_id uuid not null references contracts(id),
    currency char(3) not null,
    amount_cents bigint not null check (amount_cents >= 0),
    status text not null check (
        status in (
            'draft',
            'awaiting_approval',
            'ready',
            'posting',
            'posted',
            'failed',
            'cancelled'
        )
    ),
    invoice_recon_ref text,
    pdf_url text,
    pdf_snapshot_json jsonb,
    period_start date not null,
    period_end date not null,
    created_at timestamptz not null default now(),
    created_by_user_id uuid references users(id),
    approved_at timestamptz,
    approved_by_user_id uuid references users(id),
    posted_at timestamptz,
    last_error text,
    retry_count integer not null default 0,
    check (period_end >= period_start)
);

create index idx_settlements_tenant_counterparty_status
    on settlements (tenant_id, counterparty_id, status);

create index idx_settlements_tenant_status_created
    on settlements (tenant_id, status, created_at);
