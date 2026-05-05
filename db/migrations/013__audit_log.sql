create table audit_log (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    actor_kind text not null check (actor_kind in ('user', 'system', 'adapter')),
    actor_id text not null,
    action text not null,
    entity_kind text not null,
    entity_id uuid not null,
    before_state jsonb,
    after_state jsonb,
    occurred_at timestamptz not null default now()
);

create index idx_audit_log_tenant_occurred
    on audit_log (tenant_id, occurred_at desc);

create index idx_audit_log_entity_occurred
    on audit_log (entity_kind, entity_id, occurred_at desc);
