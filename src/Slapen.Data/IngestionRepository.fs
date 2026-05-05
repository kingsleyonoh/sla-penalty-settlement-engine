namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type IngestedBreach =
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
      RawPayloadJson: string }

type ContractClauseResolution = { ContractId: Guid; SlaClauseId: Guid }

[<RequireQualifiedAccess>]
module IngestionRepository =
    let private readBreachRow (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          ContractId = reader.GetGuid(reader.GetOrdinal "contract_id")
          SlaClauseId = reader.GetGuid(reader.GetOrdinal "sla_clause_id")
          Source = reader.GetString(reader.GetOrdinal "source")
          SourceRef = Sql.stringOption reader "source_ref"
          MetricValue = reader.GetDecimal(reader.GetOrdinal "metric_value")
          UnitsMissed =
            let ordinal = reader.GetOrdinal "units_missed"

            if reader.IsDBNull ordinal then
                None
            else
                Some(reader.GetDecimal ordinal)
          ObservedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "observed_at")
          ReportedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "reported_at")
          RawPayloadJson = reader.GetString(reader.GetOrdinal "raw_payload") }

    let startRun (dataSource: NpgsqlDataSource) (scope: TenantScope) runId source startedAt =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into ingestion_runs (
                        id, tenant_id, source, status, started_at
                    )
                    values (
                        @id, @tenant_id, @source, 'running', @started_at
                    )
                    """,
                    connection
                )

            Sql.addParameter command "id" runId
            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "source" source
            Sql.addParameter command "started_at" startedAt
            let! _ = command.ExecuteNonQueryAsync()
            return ()
        }

    let finishRun
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        runId
        status
        attempted
        stored
        rejected
        error
        finishedAt
        =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update ingestion_runs
                    set status = @status,
                        events_attempted = @attempted,
                        events_stored = @stored,
                        events_rejected = @rejected,
                        error = @error,
                        finished_at = @finished_at
                    where tenant_id = @tenant_id and id = @id
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "id" runId
            Sql.addParameter command "status" status
            Sql.addParameter command "attempted" attempted
            Sql.addParameter command "stored" stored
            Sql.addParameter command "rejected" rejected
            Sql.addOptionalParameter command "error" (error |> Option.map box)
            Sql.addParameter command "finished_at" finishedAt
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }

    let insertBreach
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breach: IngestedBreach)
        : Task<IngestedBreach option> =
        task {
            if breach.TenantId <> TenantScope.value scope then
                invalidArg (nameof breach) "Breach tenant must match tenant scope."

            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into breach_events (
                        id, tenant_id, contract_id, sla_clause_id, source, source_ref,
                        metric_value, units_missed, observed_at, reported_at,
                        raw_payload, status, created_at, updated_at
                    )
                    select
                        @id, @tenant_id, @contract_id, @sla_clause_id, @source,
                        @source_ref, @metric_value, @units_missed, @observed_at,
                        @reported_at, cast(@raw_payload as jsonb), 'pending',
                        @created_at, @created_at
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
                    on conflict (tenant_id, source, source_ref)
                    where source_ref is not null
                    do nothing
                    returning id, tenant_id, contract_id, sla_clause_id, source,
                              source_ref, metric_value, units_missed, observed_at,
                              reported_at, raw_payload::text as raw_payload
                    """,
                    connection
                )

            Sql.addParameter command "id" breach.Id
            Sql.addParameter command "tenant_id" breach.TenantId
            Sql.addParameter command "contract_id" breach.ContractId
            Sql.addParameter command "sla_clause_id" breach.SlaClauseId
            Sql.addParameter command "source" breach.Source
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

    let resolveContractClause
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (contractRef: string)
        (clauseRef: string)
        : Task<ContractClauseResolution option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select c.id as contract_id, sc.id as sla_clause_id
                    from contracts c
                    join sla_clauses sc
                      on sc.tenant_id = c.tenant_id
                     and sc.contract_id = c.id
                    where c.tenant_id = @tenant_id
                      and (c.external_ref = @contract_ref or c.reference = @contract_ref)
                      and sc.reference = @clause_ref
                    order by c.created_at desc
                    limit 1
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "contract_ref" contractRef
            Sql.addParameter command "clause_ref" clauseRef
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return
                    Some
                        { ContractId = reader.GetGuid(reader.GetOrdinal "contract_id")
                          SlaClauseId = reader.GetGuid(reader.GetOrdinal "sla_clause_id") }
            else
                return None
        }
