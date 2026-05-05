namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql
open Slapen.Domain

type BreachAccrualContext =
    { Contract: Contract
      Clause: SlaClause
      Breach: BreachEvent
      PreviousAccruals: LedgerEntryCandidate list
      PriorMeasurementWindowBreachCount: int }

[<RequireQualifiedAccess>]
module BreachEventsRepository =
    let private dateOption (reader: DbDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(DateOnly.FromDateTime(reader.GetDateTime ordinal))

    let private decimalOption (reader: DbDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetDecimal ordinal)

    let private moneyOption currency (reader: DbDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(DomainMapping.money (reader.GetInt64 ordinal) currency)

    let private measurementWindowStart window (observedAt: DateTimeOffset) =
        let dateAtMidnight = DateTimeOffset(observedAt.Date, observedAt.Offset)

        match window with
        | MeasurementWindow.Daily -> dateAtMidnight
        | MeasurementWindow.Weekly ->
            let offset = (int observedAt.DayOfWeek + 6) % 7
            dateAtMidnight.AddDays(float -offset)
        | MeasurementWindow.Monthly -> DateTimeOffset(observedAt.Year, observedAt.Month, 1, 0, 0, 0, observedAt.Offset)
        | MeasurementWindow.Quarterly ->
            let firstMonth = ((observedAt.Month - 1) / 3) * 3 + 1
            DateTimeOffset(observedAt.Year, firstMonth, 1, 0, 0, 0, observedAt.Offset)
        | MeasurementWindow.PerIncident -> observedAt

    let private readContext (reader: DbDataReader) =
        let contractCurrency = reader.GetString(reader.GetOrdinal "contract_currency")
        let penaltyType = reader.GetString(reader.GetOrdinal "penalty_type")
        let penaltyConfig = reader.GetString(reader.GetOrdinal "penalty_config")

        let clauseWindow =
            DomainMapping.measurementWindowFromText (reader.GetString(reader.GetOrdinal "measurement_window"))

        let contract =
            { Id = reader.GetGuid(reader.GetOrdinal "contract_id")
              TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
              CounterpartyId = reader.GetGuid(reader.GetOrdinal "counterparty_id")
              Reference = reader.GetString(reader.GetOrdinal "contract_reference")
              Currency =
                CurrencyCode.create contractCurrency
                |> Result.defaultWith (fun error -> failwithf "%A" error)
              EffectiveDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal "effective_date"))
              ExpiryDate = dateOption reader "expiry_date"
              Status = DomainMapping.contractStatusFromText (reader.GetString(reader.GetOrdinal "contract_status")) }

        let clause =
            { Id = reader.GetGuid(reader.GetOrdinal "sla_clause_id")
              TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
              ContractId = contract.Id
              Reference = reader.GetString(reader.GetOrdinal "clause_reference")
              Metric = reader.GetString(reader.GetOrdinal "metric")
              MeasurementWindow = clauseWindow
              TargetValue = reader.GetDecimal(reader.GetOrdinal "target_value")
              PenaltyConfig = DomainMapping.penaltyConfigFromJson penaltyType penaltyConfig
              CapPerPeriod = moneyOption contractCurrency reader "cap_per_period_cents"
              CapPerContract = moneyOption contractCurrency reader "cap_per_contract_cents"
              AccrualStartFrom =
                DomainMapping.accrualStartFromText (reader.GetString(reader.GetOrdinal "accrual_start_from"))
              Active = reader.GetBoolean(reader.GetOrdinal "active") }

        let breach =
            { Id = reader.GetGuid(reader.GetOrdinal "breach_id")
              TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
              ContractId = contract.Id
              SlaClauseId = clause.Id
              MetricValue = reader.GetDecimal(reader.GetOrdinal "metric_value")
              UnitsMissed = decimalOption reader "units_missed"
              ObservedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "observed_at")
              ReportedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "reported_at")
              ResolvedAt = None
              Status = DomainMapping.breachStatusFromText (reader.GetString(reader.GetOrdinal "breach_status")) }

        contract, clause, breach

    let private findBaseContext (dataSource: NpgsqlDataSource) scope breachEventId =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select
                        b.id as breach_id,
                        b.tenant_id,
                        b.metric_value,
                        b.units_missed,
                        b.observed_at,
                        b.reported_at,
                        b.status as breach_status,
                        c.id as contract_id,
                        c.counterparty_id,
                        c.reference as contract_reference,
                        c.currency as contract_currency,
                        c.effective_date,
                        c.expiry_date,
                        c.status as contract_status,
                        sc.id as sla_clause_id,
                        sc.reference as clause_reference,
                        sc.metric,
                        sc.measurement_window,
                        sc.target_value,
                        sc.penalty_type,
                        sc.penalty_config::text,
                        sc.cap_per_period_cents,
                        sc.cap_per_contract_cents,
                        sc.accrual_start_from,
                        sc.active
                    from breach_events b
                    join contracts c on c.tenant_id = b.tenant_id and c.id = b.contract_id
                    join sla_clauses sc on sc.tenant_id = b.tenant_id and sc.id = b.sla_clause_id
                    where b.tenant_id = @tenant_id and b.id = @breach_id
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "breach_id" breachEventId
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readContext reader)
            else
                return None
        }

    let private priorBreachCount (dataSource: NpgsqlDataSource) scope (clause: SlaClause) (breach: BreachEvent) =
        task {
            let windowStart = measurementWindowStart clause.MeasurementWindow breach.ObservedAt
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select count(*)::bigint
                    from breach_events
                    where tenant_id = @tenant_id
                      and sla_clause_id = @clause_id
                      and id <> @breach_id
                      and observed_at >= @window_start
                      and observed_at < @observed_at
                      and status in ('accrued', 'disputed', 'withdrawn', 'superseded')
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "clause_id" clause.Id
            Sql.addParameter command "breach_id" breach.Id
            Sql.addParameter command "window_start" windowStart
            Sql.addParameter command "observed_at" breach.ObservedAt
            let! count = command.ExecuteScalarAsync()
            return Convert.ToInt32(count)
        }

    let findAccrualContext
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        : Task<BreachAccrualContext option> =
        task {
            let! baseContext = findBaseContext dataSource scope breachEventId

            match baseContext with
            | None -> return None
            | Some(contract, clause, breach) ->
                let! previousAccruals = PenaltyLedgerRepository.listActiveAccrualsByClause dataSource scope clause.Id

                let! priorCount = priorBreachCount dataSource scope clause breach

                return
                    Some
                        { Contract = contract
                          Clause = clause
                          Breach = breach
                          PreviousAccruals = previousAccruals
                          PriorMeasurementWindowBreachCount = priorCount }
        }

    let updateStatus
        (connection: NpgsqlConnection)
        (transaction: NpgsqlTransaction)
        (scope: TenantScope)
        (breachEventId: Guid)
        (expectedStatus: BreachStatus)
        (newStatus: BreachStatus)
        (updatedAt: DateTimeOffset)
        =
        task {
            use command =
                new NpgsqlCommand(
                    """
                    update breach_events
                    set status = @new_status, updated_at = @updated_at
                    where tenant_id = @tenant_id and id = @breach_id and status = @expected_status
                    """,
                    connection,
                    transaction
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "breach_id" breachEventId
            Sql.addParameter command "expected_status" (DomainMapping.breachStatusText expectedStatus)
            Sql.addParameter command "new_status" (DomainMapping.breachStatusText newStatus)
            Sql.addParameter command "updated_at" updatedAt
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }
