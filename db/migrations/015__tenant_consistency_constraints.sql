create unique index ux_counterparties_tenant_id_id on counterparties (tenant_id, id);
create unique index ux_contracts_tenant_id_id on contracts (tenant_id, id);
create unique index ux_sla_clauses_tenant_id_id on sla_clauses (tenant_id, id);
create unique index ux_breach_events_tenant_id_id on breach_events (tenant_id, id);
create unique index ux_penalty_ledger_tenant_id_id on penalty_ledger (tenant_id, id);
create unique index ux_settlements_tenant_id_id on settlements (tenant_id, id);
create unique index ux_users_tenant_id_id on users (tenant_id, id);

alter table contracts
    add constraint fk_contracts_counterparty_same_tenant
    foreign key (tenant_id, counterparty_id)
    references counterparties (tenant_id, id);

alter table sla_clauses
    add constraint fk_sla_clauses_contract_same_tenant
    foreign key (tenant_id, contract_id)
    references contracts (tenant_id, id);

alter table breach_events
    add constraint fk_breach_events_contract_same_tenant
    foreign key (tenant_id, contract_id)
    references contracts (tenant_id, id),
    add constraint fk_breach_events_clause_same_tenant
    foreign key (tenant_id, sla_clause_id)
    references sla_clauses (tenant_id, id);

alter table penalty_ledger
    add constraint fk_penalty_ledger_clause_same_tenant
    foreign key (tenant_id, sla_clause_id)
    references sla_clauses (tenant_id, id),
    add constraint fk_penalty_ledger_breach_same_tenant
    foreign key (tenant_id, breach_event_id)
    references breach_events (tenant_id, id),
    add constraint fk_penalty_ledger_counterparty_same_tenant
    foreign key (tenant_id, counterparty_id)
    references counterparties (tenant_id, id),
    add constraint fk_penalty_ledger_contract_same_tenant
    foreign key (tenant_id, contract_id)
    references contracts (tenant_id, id);
