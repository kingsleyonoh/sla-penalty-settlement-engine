create table ingestion_adapter_settings (
    tenant_id uuid not null references tenants(id),
    adapter text not null check (adapter in (
        'manual',
        'csv_import',
        'contract_lifecycle_rest',
        'contract_lifecycle_nats',
        'hub_ingress'
    )),
    enabled boolean not null default false,
    poll_interval_seconds integer not null default 900 check (poll_interval_seconds > 0),
    last_tested_at timestamptz,
    last_test_status text check (last_test_status in ('healthy', 'disabled', 'failed')),
    last_test_error text,
    last_pull_requested_at timestamptz,
    updated_at timestamptz not null default now(),
    primary key (tenant_id, adapter)
);

create index idx_ingestion_adapter_settings_enabled
    on ingestion_adapter_settings (tenant_id, enabled);
