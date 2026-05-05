insert into counterparties (
    id,
    tenant_id,
    canonical_name,
    normalized_name,
    tax_id,
    country_code,
    external_refs,
    default_currency
)
values
    (
        '11000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001',
        'Nordlicht Services GmbH',
        'nordlicht services gmbh',
        'DE987654321',
        'DE',
        '{"contract_lifecycle":"cl-acme-nordlicht","invoice_recon":"ir-acme-nordlicht","vpi":"vpi-acme-nordlicht"}',
        'EUR'
    ),
    (
        '22000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        'Pacific Support LLC',
        'pacific support llc',
        '98-7654321',
        'US',
        '{"contract_lifecycle":"cl-globex-pacific","invoice_recon":"ir-globex-pacific","vpi":"vpi-globex-pacific"}',
        'USD'
    )
on conflict (id) do nothing;
