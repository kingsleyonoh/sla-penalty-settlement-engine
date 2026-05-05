namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql
open Slapen.Domain

type LedgerEntrySummary =
    { Entry: LedgerEntryCandidate
      ContractReference: string
      ClauseReference: string
      CounterpartyName: string
      BreachStatus: string }

[<RequireQualifiedAccess>]
module PenaltyLedgerRepository =
    let private addCandidateParameters (command: NpgsqlCommand) (entry: LedgerEntryCandidate) =
        let createdByKind, createdByUserId = DomainMapping.createdByParts entry.CreatedBy

        Sql.addParameter command "id" entry.Id
        Sql.addParameter command "tenant_id" entry.TenantId
        Sql.addParameter command "sla_clause_id" entry.SlaClauseId
        Sql.addParameter command "breach_event_id" entry.BreachEventId
        Sql.addParameter command "counterparty_id" entry.CounterpartyId
        Sql.addParameter command "contract_id" entry.ContractId
        Sql.addParameter command "entry_kind" (DomainMapping.ledgerEntryKindText entry.EntryKind)
        Sql.addParameter command "direction" (DomainMapping.ledgerDirectionText entry.Direction)
        Sql.addParameter command "amount_cents" (Money.cents entry.Amount)
        Sql.addParameter command "currency" (Money.currency entry.Amount)
        Sql.addParameter command "accrual_period_start" entry.AccrualPeriodStart
        Sql.addParameter command "accrual_period_end" entry.AccrualPeriodEnd
        Sql.addOptionalParameter command "compensates_ledger_id" (entry.CompensatesLedgerId |> Option.map box)
        Sql.addParameter command "reason_code" (DomainMapping.reasonCodeText entry.ReasonCode)
        Sql.addOptionalParameter command "reason_notes" (entry.ReasonNotes |> Option.map box)
        Sql.addParameter command "created_at" entry.CreatedAt
        Sql.addParameter command "created_by_kind" createdByKind
        Sql.addOptionalParameter command "created_by_user_id" (createdByUserId |> Option.map box)

    let private insertOne
        (connection: NpgsqlConnection)
        (transaction: NpgsqlTransaction)
        (scope: TenantScope)
        (entry: LedgerEntryCandidate)
        =
        task {
            if entry.TenantId <> TenantScope.value scope then
                invalidArg (nameof entry) "Ledger entry tenant must match tenant scope."

            use command =
                new NpgsqlCommand(
                    """
                    insert into penalty_ledger (
                        id,
                        tenant_id,
                        sla_clause_id,
                        breach_event_id,
                        counterparty_id,
                        contract_id,
                        entry_kind,
                        direction,
                        amount_cents,
                        currency,
                        accrual_period_start,
                        accrual_period_end,
                        compensates_ledger_id,
                        reason_code,
                        reason_notes,
                        created_at,
                        created_by_kind,
                        created_by_user_id
                    )
                    values (
                        @id,
                        @tenant_id,
                        @sla_clause_id,
                        @breach_event_id,
                        @counterparty_id,
                        @contract_id,
                        @entry_kind,
                        @direction,
                        @amount_cents,
                        @currency,
                        @accrual_period_start,
                        @accrual_period_end,
                        @compensates_ledger_id,
                        @reason_code,
                        @reason_notes,
                        @created_at,
                        @created_by_kind,
                        @created_by_user_id
                    )
                    """,
                    connection,
                    transaction
                )

            addCandidateParameters command entry
            let! _ = command.ExecuteNonQueryAsync()
            return entry.Id
        }

    let insertPair
        (connection: NpgsqlConnection)
        (transaction: NpgsqlTransaction)
        (scope: TenantScope)
        (pair: LedgerPair)
        : Task<Guid list> =
        task {
            let ids = ResizeArray<Guid>()

            for entry in LedgerPair.entries pair do
                let! id = insertOne connection transaction scope entry.Snapshot
                ids.Add id

            return List.ofSeq ids
        }

    let private readLedgerEntry (reader: DbDataReader) =
        let cents = reader.GetInt64(reader.GetOrdinal "amount_cents")
        let currency = reader.GetString(reader.GetOrdinal "currency")
        let userId = Sql.guidOption reader "created_by_user_id"

        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          ContractId = reader.GetGuid(reader.GetOrdinal "contract_id")
          CounterpartyId = reader.GetGuid(reader.GetOrdinal "counterparty_id")
          SlaClauseId = reader.GetGuid(reader.GetOrdinal "sla_clause_id")
          BreachEventId = reader.GetGuid(reader.GetOrdinal "breach_event_id")
          EntryKind = DomainMapping.ledgerEntryKindFromText (reader.GetString(reader.GetOrdinal "entry_kind"))
          Direction = DomainMapping.ledgerDirectionFromText (reader.GetString(reader.GetOrdinal "direction"))
          Amount = DomainMapping.money cents currency
          AccrualPeriodStart = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "accrual_period_start")
          AccrualPeriodEnd = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "accrual_period_end")
          CompensatesLedgerId = Sql.guidOption reader "compensates_ledger_id"
          ReasonCode = DomainMapping.reasonCodeFromText (reader.GetString(reader.GetOrdinal "reason_code"))
          ReasonNotes = Sql.stringOption reader "reason_notes"
          CreatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "created_at")
          CreatedBy = DomainMapping.createdByFromText (reader.GetString(reader.GetOrdinal "created_by_kind")) userId }

    let private readLedgerSummary (reader: DbDataReader) =
        { Entry = readLedgerEntry reader
          ContractReference = reader.GetString(reader.GetOrdinal "contract_reference")
          ClauseReference = reader.GetString(reader.GetOrdinal "clause_reference")
          CounterpartyName = reader.GetString(reader.GetOrdinal "counterparty_name")
          BreachStatus = reader.GetString(reader.GetOrdinal "breach_status") }

    let listByBreach
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        : Task<LedgerEntryCandidate list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select
                        id,
                        tenant_id,
                        sla_clause_id,
                        breach_event_id,
                        counterparty_id,
                        contract_id,
                        entry_kind,
                        direction,
                        amount_cents,
                        currency,
                        accrual_period_start,
                        accrual_period_end,
                        compensates_ledger_id,
                        reason_code,
                        reason_notes,
                        created_at,
                        created_by_kind,
                        created_by_user_id
                    from penalty_ledger
                    where tenant_id = @tenant_id and breach_event_id = @breach_event_id
                    order by created_at, direction
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "breach_event_id" breachEventId
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<LedgerEntryCandidate>()

            while reader.Read() do
                rows.Add(readLedgerEntry reader)

            return List.ofSeq rows
        }

    let listForTenant (dataSource: NpgsqlDataSource) (scope: TenantScope) (limit: int) : Task<LedgerEntrySummary list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select
                        pl.id,
                        pl.tenant_id,
                        pl.sla_clause_id,
                        pl.breach_event_id,
                        pl.counterparty_id,
                        pl.contract_id,
                        pl.entry_kind,
                        pl.direction,
                        pl.amount_cents,
                        pl.currency,
                        pl.accrual_period_start,
                        pl.accrual_period_end,
                        pl.compensates_ledger_id,
                        pl.reason_code,
                        pl.reason_notes,
                        pl.created_at,
                        pl.created_by_kind,
                        pl.created_by_user_id,
                        c.reference as contract_reference,
                        sc.reference as clause_reference,
                        cp.canonical_name as counterparty_name,
                        be.status as breach_status
                    from penalty_ledger pl
                    join contracts c on c.tenant_id = pl.tenant_id and c.id = pl.contract_id
                    join sla_clauses sc on sc.tenant_id = pl.tenant_id and sc.id = pl.sla_clause_id
                    join counterparties cp on cp.tenant_id = pl.tenant_id and cp.id = pl.counterparty_id
                    join breach_events be on be.tenant_id = pl.tenant_id and be.id = pl.breach_event_id
                    where pl.tenant_id = @tenant_id
                    order by pl.created_at desc, pl.direction
                    limit @limit
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "limit" limit
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<LedgerEntrySummary>()

            while reader.Read() do
                rows.Add(readLedgerSummary reader)

            return List.ofSeq rows
        }

    let listActiveAccrualsByClause
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (slaClauseId: Guid)
        : Task<LedgerEntryCandidate list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select
                        pl.id,
                        pl.tenant_id,
                        pl.sla_clause_id,
                        pl.breach_event_id,
                        pl.counterparty_id,
                        pl.contract_id,
                        pl.entry_kind,
                        pl.direction,
                        pl.amount_cents,
                        pl.currency,
                        pl.accrual_period_start,
                        pl.accrual_period_end,
                        pl.compensates_ledger_id,
                        pl.reason_code,
                        pl.reason_notes,
                        pl.created_at,
                        pl.created_by_kind,
                        pl.created_by_user_id
                    from penalty_ledger pl
                    where pl.tenant_id = @tenant_id
                      and pl.sla_clause_id = @sla_clause_id
                      and pl.entry_kind = 'accrual'
                      and not exists (
                          select 1
                          from penalty_ledger rev
                          where rev.tenant_id = pl.tenant_id
                            and rev.entry_kind = 'reversal'
                            and rev.compensates_ledger_id = pl.id
                      )
                    order by pl.created_at, pl.direction
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "sla_clause_id" slaClauseId
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<LedgerEntryCandidate>()

            while reader.Read() do
                rows.Add(readLedgerEntry reader)

            return List.ofSeq rows
        }
