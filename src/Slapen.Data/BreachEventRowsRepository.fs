namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type BreachEventRow =
    { Id: Guid
      TenantId: Guid
      ContractId: Guid
      SlaClauseId: Guid
      Source: string
      SourceRef: string option
      MetricValue: decimal
      UnitsMissed: decimal option
      ObservedAt: DateTimeOffset
      ReportedAt: DateTimeOffset
      Status: string }

type NewManualBreach =
    { Id: Guid
      TenantId: Guid
      ContractId: Guid
      SlaClauseId: Guid
      SourceRef: string option
      MetricValue: decimal
      UnitsMissed: decimal option
      ObservedAt: DateTimeOffset
      ReportedAt: DateTimeOffset
      RawPayloadJson: string }

[<RequireQualifiedAccess>]
module BreachEventRowsRepository =
    let private decimalOption (reader: DbDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetDecimal ordinal)

    let private readBreachRow (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          ContractId = reader.GetGuid(reader.GetOrdinal "contract_id")
          SlaClauseId = reader.GetGuid(reader.GetOrdinal "sla_clause_id")
          Source = reader.GetString(reader.GetOrdinal "source")
          SourceRef = Sql.stringOption reader "source_ref"
          MetricValue = reader.GetDecimal(reader.GetOrdinal "metric_value")
          UnitsMissed = decimalOption reader "units_missed"
          ObservedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "observed_at")
          ReportedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "reported_at")
          Status = reader.GetString(reader.GetOrdinal "status") }

    let private selectBreachRow =
        """
        select
            id,
            tenant_id,
            contract_id,
            sla_clause_id,
            source,
            source_ref,
            metric_value,
            units_missed,
            observed_at,
            reported_at,
            status
        from breach_events
        """

    let findById
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        : Task<BreachEventRow option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    selectBreachRow
                    + """
                    where tenant_id = @tenant_id and id = @breach_id
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "breach_id" breachEventId
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readBreachRow reader)
            else
                return None
        }

    let createManual
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breach: NewManualBreach)
        : Task<BreachEventRow option> =
        task {
            if breach.TenantId <> TenantScope.value scope then
                invalidArg (nameof breach) "Breach tenant must match tenant scope."

            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into breach_events (
                        id,
                        tenant_id,
                        contract_id,
                        sla_clause_id,
                        source,
                        source_ref,
                        metric_value,
                        units_missed,
                        observed_at,
                        reported_at,
                        raw_payload,
                        status,
                        created_at,
                        updated_at
                    )
                    select
                        @id,
                        @tenant_id,
                        @contract_id,
                        @sla_clause_id,
                        'manual',
                        @source_ref,
                        @metric_value,
                        @units_missed,
                        @observed_at,
                        @reported_at,
                        cast(@raw_payload as jsonb),
                        'pending',
                        @created_at,
                        @created_at
                    where exists (
                        select 1
                        from contracts c
                        join sla_clauses sc
                          on sc.tenant_id = c.tenant_id
                         and sc.contract_id = c.id
                         and sc.id = @sla_clause_id
                        where c.tenant_id = @tenant_id
                          and c.id = @contract_id
                    )
                    returning
                        id,
                        tenant_id,
                        contract_id,
                        sla_clause_id,
                        source,
                        source_ref,
                        metric_value,
                        units_missed,
                        observed_at,
                        reported_at,
                        status
                    """,
                    connection
                )

            Sql.addParameter command "id" breach.Id
            Sql.addParameter command "tenant_id" breach.TenantId
            Sql.addParameter command "contract_id" breach.ContractId
            Sql.addParameter command "sla_clause_id" breach.SlaClauseId
            Sql.addOptionalParameter command "source_ref" (breach.SourceRef |> Option.map box)
            Sql.addParameter command "metric_value" breach.MetricValue
            Sql.addOptionalParameter command "units_missed" (breach.UnitsMissed |> Option.map box)
            Sql.addParameter command "observed_at" breach.ObservedAt
            Sql.addParameter command "reported_at" breach.ReportedAt
            Sql.addParameter command "raw_payload" breach.RawPayloadJson
            Sql.addParameter command "created_at" DateTimeOffset.UtcNow
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readBreachRow reader)
            else
                return None
        }
