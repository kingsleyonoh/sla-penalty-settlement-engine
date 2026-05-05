insert into signal_definitions (
    code,
    category,
    direction,
    value_type,
    default_weight
)
values
    ('supplier.penalty.accrued', 'supplier_penalty', 'negative', 'numeric', 1.0000),
    ('supplier.penalty.paid_on_time', 'supplier_penalty', 'positive', 'boolean', 0.7500),
    ('supplier.dispute.won_against_us', 'supplier_dispute', 'negative', 'boolean', 1.2500)
on conflict (code) do update set
    category = excluded.category,
    direction = excluded.direction,
    value_type = excluded.value_type,
    default_weight = excluded.default_weight;
