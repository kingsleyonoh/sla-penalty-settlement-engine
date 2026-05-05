create table signal_definitions (
    code text primary key,
    category text not null,
    direction text not null check (direction in ('positive', 'negative', 'neutral')),
    value_type text not null check (value_type in ('numeric', 'boolean')),
    default_weight numeric(6,4) not null default 1.0
);
