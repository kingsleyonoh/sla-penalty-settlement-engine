insert into contracts (
    id,
    tenant_id,
    counterparty_id,
    reference,
    title,
    source,
    external_ref,
    currency,
    effective_date,
    expiry_date,
    status,
    document_url
)
values
    (
        '12000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001',
        '11000000-0000-0000-0000-000000000001',
        'ACME-MSA-2026-001',
        'Managed Support Services Germany',
        'manual',
        'contract-acme-001',
        'EUR',
        '2026-01-01',
        '2026-12-31',
        'active',
        'https://contracts.example.test/acme-001.pdf'
    ),
    (
        '23000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '22000000-0000-0000-0000-000000000001',
        'GLOBEX-MSA-2026-009',
        'North America Response Desk',
        'manual',
        'contract-globex-009',
        'USD',
        '2026-02-01',
        '2027-01-31',
        'active',
        'https://contracts.example.test/globex-009.pdf'
    )
on conflict (id) do nothing;

insert into sla_clauses (
    id,
    tenant_id,
    contract_id,
    reference,
    metric,
    measurement_window,
    target_value,
    penalty_type,
    penalty_config,
    cap_per_period_cents,
    cap_per_contract_cents,
    accrual_start_from,
    active
)
values
    (
        '13000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001',
        '12000000-0000-0000-0000-000000000001',
        'Schedule B 2.3.1',
        'response_time_minutes',
        'per_incident',
        60.0000,
        'flat_per_breach',
        '{"amount_cents":50000,"currency":"EUR"}',
        250000,
        1000000,
        'breach_observed_at',
        true
    ),
    (
        '24000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '23000000-0000-0000-0000-000000000001',
        'Exhibit C 4.2',
        'ticket_resolution_hours',
        'weekly',
        24.0000,
        'linear_per_unit_missed',
        '{"amount_per_unit_cents":7500,"unit_label":"ticket","currency":"USD"}',
        300000,
        1200000,
        'breach_reported_at',
        true
    )
on conflict (id) do nothing;
