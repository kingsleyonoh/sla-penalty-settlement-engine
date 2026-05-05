alter table settlements
    add constraint fk_settlements_counterparty_same_tenant
    foreign key (tenant_id, counterparty_id)
    references counterparties (tenant_id, id),
    add constraint fk_settlements_contract_same_tenant
    foreign key (tenant_id, contract_id)
    references contracts (tenant_id, id);

alter table settlement_ledger_entries
    add constraint fk_settlement_entries_settlement_same_tenant
    foreign key (tenant_id, settlement_id)
    references settlements (tenant_id, id),
    add constraint fk_settlement_entries_ledger_same_tenant
    foreign key (tenant_id, ledger_entry_id)
    references penalty_ledger (tenant_id, id);

alter table breach_events
    add constraint fk_breach_events_disputed_user_same_tenant
    foreign key (tenant_id, disputed_by_user_id)
    references users (tenant_id, id);

alter table penalty_ledger
    add constraint fk_penalty_ledger_created_user_same_tenant
    foreign key (tenant_id, created_by_user_id)
    references users (tenant_id, id);

alter table settlements
    add constraint fk_settlements_created_user_same_tenant
    foreign key (tenant_id, created_by_user_id)
    references users (tenant_id, id),
    add constraint fk_settlements_approved_user_same_tenant
    foreign key (tenant_id, approved_by_user_id)
    references users (tenant_id, id);
