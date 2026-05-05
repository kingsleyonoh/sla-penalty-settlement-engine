create table ingestion_runs (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    source text not null,
    status text not null check (status in ('running', 'succeeded', 'failed', 'partial')),
    events_attempted integer not null default 0,
    events_stored integer not null default 0,
    events_rejected integer not null default 0,
    cursor jsonb,
    started_at timestamptz not null default now(),
    finished_at timestamptz,
    error text
);

create index idx_ingestion_runs_tenant_source_started
    on ingestion_runs (tenant_id, source, started_at desc);
