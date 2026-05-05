alter table outbox
    add column idempotency_key text,
    add column locked_until timestamptz,
    add column locked_by text;

create unique index ux_outbox_tenant_idempotency_key
    on outbox (tenant_id, idempotency_key)
    where idempotency_key is not null;

