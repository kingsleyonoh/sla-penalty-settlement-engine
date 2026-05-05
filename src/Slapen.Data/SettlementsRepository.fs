namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type UncommittedSettlementLedgerEntry =
    { LedgerEntryId: Guid
      TenantId: Guid
      CounterpartyId: Guid
      CounterpartyName: string
      CounterpartyTaxId: string option
      CounterpartyCountryCode: string option
      ContractId: Guid
      ContractReference: string
      ContractTitle: string
      SlaClauseId: Guid
      ClauseReference: string
      BreachEventId: Guid
      AmountCents: int64
      Currency: string
      AccrualPeriodStart: DateTimeOffset
      AccrualPeriodEnd: DateTimeOffset }

type NewSettlement =
    { Id: Guid
      TenantId: Guid
      CounterpartyId: Guid
      ContractId: Guid
      Currency: string
      AmountCents: int64
      PdfSnapshotJson: string
      PeriodStart: DateOnly
      PeriodEnd: DateOnly
      CreatedAt: DateTimeOffset
      CreatedByUserId: Guid option
      LedgerEntryIds: Guid list }

type SettlementRecord =
    { Id: Guid
      TenantId: Guid
      CounterpartyId: Guid
      ContractId: Guid
      Currency: string
      AmountCents: int64
      Status: string
      PdfUrl: string option
      PdfSnapshotJson: string option }

[<RequireQualifiedAccess>]
module SettlementsRepository =
    let private readUncommitted (reader: DbDataReader) =
        { LedgerEntryId = reader.GetGuid(reader.GetOrdinal "ledger_entry_id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          CounterpartyId = reader.GetGuid(reader.GetOrdinal "counterparty_id")
          CounterpartyName = reader.GetString(reader.GetOrdinal "counterparty_name")
          CounterpartyTaxId = Sql.stringOption reader "counterparty_tax_id"
          CounterpartyCountryCode = Sql.stringOption reader "counterparty_country_code"
          ContractId = reader.GetGuid(reader.GetOrdinal "contract_id")
          ContractReference = reader.GetString(reader.GetOrdinal "contract_reference")
          ContractTitle = reader.GetString(reader.GetOrdinal "contract_title")
          SlaClauseId = reader.GetGuid(reader.GetOrdinal "sla_clause_id")
          ClauseReference = reader.GetString(reader.GetOrdinal "clause_reference")
          BreachEventId = reader.GetGuid(reader.GetOrdinal "breach_event_id")
          AmountCents = reader.GetInt64(reader.GetOrdinal "amount_cents")
          Currency = reader.GetString(reader.GetOrdinal "currency")
          AccrualPeriodStart = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "accrual_period_start")
          AccrualPeriodEnd = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "accrual_period_end") }

    let private readSettlement (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          CounterpartyId = reader.GetGuid(reader.GetOrdinal "counterparty_id")
          ContractId = reader.GetGuid(reader.GetOrdinal "contract_id")
          Currency = reader.GetString(reader.GetOrdinal "currency")
          AmountCents = reader.GetInt64(reader.GetOrdinal "amount_cents")
          Status = reader.GetString(reader.GetOrdinal "status")
          PdfUrl = Sql.stringOption reader "pdf_url"
          PdfSnapshotJson = Sql.stringOption reader "pdf_snapshot_json" }

    let listUncommittedAccruals
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (periodStart: DateOnly)
        (periodEnd: DateOnly)
        (asOf: DateTimeOffset)
        : Task<UncommittedSettlementLedgerEntry list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select
                        pl.id as ledger_entry_id,
                        pl.tenant_id,
                        pl.counterparty_id,
                        cp.canonical_name as counterparty_name,
                        cp.tax_id as counterparty_tax_id,
                        cp.country_code as counterparty_country_code,
                        pl.contract_id,
                        c.reference as contract_reference,
                        c.title as contract_title,
                        pl.sla_clause_id,
                        sc.reference as clause_reference,
                        pl.breach_event_id,
                        pl.amount_cents,
                        pl.currency,
                        pl.accrual_period_start,
                        pl.accrual_period_end
                    from penalty_ledger pl
                    join counterparties cp on cp.tenant_id = pl.tenant_id and cp.id = pl.counterparty_id
                    join contracts c on c.tenant_id = pl.tenant_id and c.id = pl.contract_id
                    join sla_clauses sc on sc.tenant_id = pl.tenant_id and sc.id = pl.sla_clause_id
                    where pl.tenant_id = @tenant_id
                      and pl.entry_kind = 'accrual'
                      and pl.direction = 'credit_owed_to_us'
                      and pl.created_at <= @as_of
                      and pl.accrual_period_start::date >= @period_start
                      and pl.accrual_period_end::date <= @period_end
                      and not exists (
                          select 1 from settlement_ledger_entries sle
                          where sle.tenant_id = pl.tenant_id
                            and sle.ledger_entry_id = pl.id
                            and sle.released_at is null
                      )
                      and not exists (
                          select 1 from penalty_ledger rev
                          where rev.tenant_id = pl.tenant_id
                            and rev.entry_kind = 'reversal'
                            and rev.compensates_ledger_id = pl.id
                      )
                    order by cp.canonical_name, c.reference, pl.currency, pl.created_at
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "period_start" periodStart
            Sql.addParameter command "period_end" periodEnd
            Sql.addParameter command "as_of" asOf
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<UncommittedSettlementLedgerEntry>()

            while reader.Read() do
                rows.Add(readUncommitted reader)

            return List.ofSeq rows
        }

    let insert
        (connection: NpgsqlConnection)
        (transaction: NpgsqlTransaction)
        (scope: TenantScope)
        (settlement: NewSettlement)
        : Task<Guid> =
        task {
            if settlement.TenantId <> TenantScope.value scope then
                invalidArg (nameof settlement) "Settlement tenant must match tenant scope."

            use command =
                new NpgsqlCommand(
                    """
                    insert into settlements (
                        id, tenant_id, counterparty_id, contract_id, currency, amount_cents,
                        status, pdf_snapshot_json, period_start, period_end, created_at,
                        created_by_user_id
                    )
                    values (
                        @id, @tenant_id, @counterparty_id, @contract_id, @currency,
                        @amount_cents, 'ready', cast(@pdf_snapshot_json as jsonb),
                        @period_start, @period_end, @created_at, @created_by_user_id
                    )
                    """,
                    connection,
                    transaction
                )

            Sql.addParameter command "id" settlement.Id
            Sql.addParameter command "tenant_id" settlement.TenantId
            Sql.addParameter command "counterparty_id" settlement.CounterpartyId
            Sql.addParameter command "contract_id" settlement.ContractId
            Sql.addParameter command "currency" settlement.Currency
            Sql.addParameter command "amount_cents" settlement.AmountCents
            Sql.addParameter command "pdf_snapshot_json" settlement.PdfSnapshotJson
            Sql.addParameter command "period_start" settlement.PeriodStart
            Sql.addParameter command "period_end" settlement.PeriodEnd
            Sql.addParameter command "created_at" settlement.CreatedAt
            Sql.addOptionalParameter command "created_by_user_id" (settlement.CreatedByUserId |> Option.map box)
            let! _ = command.ExecuteNonQueryAsync()

            for ledgerEntryId in settlement.LedgerEntryIds do
                use membership =
                    new NpgsqlCommand(
                        """
                        insert into settlement_ledger_entries (
                            id, tenant_id, settlement_id, ledger_entry_id, role, created_at
                        )
                        values (
                            @id, @tenant_id, @settlement_id, @ledger_entry_id,
                            'included_accrual', @created_at
                        )
                        """,
                        connection,
                        transaction
                    )

                Sql.addParameter membership "id" (Guid.NewGuid())
                Sql.addParameter membership "tenant_id" settlement.TenantId
                Sql.addParameter membership "settlement_id" settlement.Id
                Sql.addParameter membership "ledger_entry_id" ledgerEntryId
                Sql.addParameter membership "created_at" settlement.CreatedAt
                let! _ = membership.ExecuteNonQueryAsync()
                ()

            return settlement.Id
        }

    let findById
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (settlementId: Guid)
        : Task<SettlementRecord option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select id, tenant_id, counterparty_id, contract_id, currency,
                           amount_cents, status, pdf_url, pdf_snapshot_json::text as pdf_snapshot_json
                    from settlements
                    where tenant_id = @tenant_id and id = @id
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "id" settlementId
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readSettlement reader)
            else
                return None
        }
