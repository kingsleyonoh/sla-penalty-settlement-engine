create table users (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    email citext not null,
    password_hash text not null,
    display_name text not null,
    role text not null check (role in ('ops', 'supervisor', 'read_only')),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, email)
);

create index idx_users_tenant_active on users (tenant_id, is_active);
