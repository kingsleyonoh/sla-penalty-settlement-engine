create table outbox (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    op text not null check (
        op in (
            'invoice_recon.post_credit_note',
            'invoice_recon.post_debit_memo',
            'hub.emit',
            'vpi.emit_signal',
            'workflow.trigger'
        )
    ),
    payload jsonb not null,
    status text not null check (status in ('pending', 'in_flight', 'done', 'failed', 'dead')),
    attempts integer not null default 0,
    last_error text,
    next_run_at timestamptz not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    check (
        op not in ('invoice_recon.post_credit_note', 'invoice_recon.post_debit_memo')
        or payload ? 'settlement_id'
    ),
    check (
        op <> 'hub.emit'
        or (payload ? 'event_type' and payload ? 'event_id' and payload ? 'payload')
    ),
    check (
        op <> 'vpi.emit_signal'
        or (
            payload ? 'signal_code'
            and payload ? 'vendor_external_ref'
            and payload ? 'value_numeric'
            and payload ? 'observed_at'
            and payload ? 'raw_context'
        )
    )
);

create index idx_outbox_status_next_run on outbox (status, next_run_at);
create index idx_outbox_tenant_status_next_run on outbox (tenant_id, status, next_run_at);
create index idx_outbox_tenant_op_created on outbox (tenant_id, op, created_at desc);
