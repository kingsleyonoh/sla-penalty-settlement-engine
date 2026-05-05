create or replace function block_penalty_ledger_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'penalty_ledger is append-only; use compensating entries'
        using errcode = 'P0001';
end;
$$;

create trigger penalty_ledger_block_update
before update on penalty_ledger
for each row execute function block_penalty_ledger_mutation();

create trigger penalty_ledger_block_delete
before delete on penalty_ledger
for each row execute function block_penalty_ledger_mutation();
