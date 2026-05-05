create extension if not exists citext;

create table tenants (
    id uuid primary key,
    name text not null,
    slug text not null unique check (char_length(slug) between 2 and 24),
    api_key_hash text not null,
    api_key_prefix text not null,
    legal_name text not null,
    full_legal_name text not null,
    display_name text not null,
    address jsonb not null,
    registration jsonb not null,
    contact jsonb not null,
    wordmark_url text,
    brand_primary_hex text,
    brand_accent_hex text,
    locale text not null default 'en-US',
    timezone text not null default 'UTC',
    default_currency char(3) not null default 'EUR',
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index idx_tenants_api_key_prefix on tenants (api_key_prefix);
