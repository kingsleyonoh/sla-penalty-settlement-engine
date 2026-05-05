namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type SlaClauseRow =
    { Id: Guid
      TenantId: Guid
      ContractId: Guid
      Reference: string
      Metric: string
      MeasurementWindow: string
      TargetValue: decimal
      PenaltyType: string
      PenaltyConfigJson: string
      Active: bool }

type NewSlaClause =
    { Id: Guid
      TenantId: Guid
      ContractId: Guid
      Reference: string
      Metric: string
      MeasurementWindow: string
      TargetValue: decimal
      PenaltyType: string
      PenaltyConfigJson: string
      CapPerPeriodCents: int64 option
      CapPerContractCents: int64 option
      AccrualStartFrom: string
      Active: bool }

[<RequireQualifiedAccess>]
module SlaClausesRepository =
    let private readClause (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          ContractId = reader.GetGuid(reader.GetOrdinal "contract_id")
          Reference = reader.GetString(reader.GetOrdinal "reference")
          Metric = reader.GetString(reader.GetOrdinal "metric")
          MeasurementWindow = reader.GetString(reader.GetOrdinal "measurement_window")
          TargetValue = reader.GetDecimal(reader.GetOrdinal "target_value")
          PenaltyType = reader.GetString(reader.GetOrdinal "penalty_type")
          PenaltyConfigJson = reader.GetString(reader.GetOrdinal "penalty_config_json")
          Active = reader.GetBoolean(reader.GetOrdinal "active") }

    let private selectSql =
        """
        select
            id,
            tenant_id,
            contract_id,
            reference,
            metric,
            measurement_window,
            target_value,
            penalty_type,
            penalty_config::text as penalty_config_json,
            active
        from sla_clauses
        """

    let listByContract
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (contractId: Guid)
        : Task<SlaClauseRow list option> =
        task {
            let! contract = ContractsRepository.findById dataSource scope contractId

            match contract with
            | None -> return None
            | Some _ ->
                use! connection = dataSource.OpenConnectionAsync().AsTask()

                use command =
                    new NpgsqlCommand(
                        selectSql
                        + """
                        where tenant_id = @tenant_id and contract_id = @contract_id
                        order by reference
                        """,
                        connection
                    )

                Sql.addParameter command "tenant_id" (TenantScope.value scope)
                Sql.addParameter command "contract_id" contractId
                use! reader = command.ExecuteReaderAsync()
                let rows = ResizeArray<SlaClauseRow>()

                while reader.Read() do
                    rows.Add(readClause reader)

                return Some(List.ofSeq rows)
        }

    let create (dataSource: NpgsqlDataSource) (scope: TenantScope) (clause: NewSlaClause) : Task<SlaClauseRow option> =
        task {
            if clause.TenantId <> TenantScope.value scope then
                invalidArg (nameof clause) "SLA clause tenant must match tenant scope."

            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into sla_clauses (
                        id,
                        tenant_id,
                        contract_id,
                        reference,
                        metric,
                        measurement_window,
                        target_value,
                        penalty_type,
                        penalty_config,
                        cap_per_period_cents,
                        cap_per_contract_cents,
                        accrual_start_from,
                        active,
                        created_at,
                        updated_at
                    )
                    select
                        @id,
                        @tenant_id,
                        @contract_id,
                        @reference,
                        @metric,
                        @measurement_window,
                        @target_value,
                        @penalty_type,
                        cast(@penalty_config as jsonb),
                        @cap_per_period_cents,
                        @cap_per_contract_cents,
                        @accrual_start_from,
                        @active,
                        @created_at,
                        @created_at
                    where exists (
                        select 1 from contracts
                        where tenant_id = @tenant_id and id = @contract_id
                    )
                    returning
                        id,
                        tenant_id,
                        contract_id,
                        reference,
                        metric,
                        measurement_window,
                        target_value,
                        penalty_type,
                        penalty_config::text as penalty_config_json,
                        active
                    """,
                    connection
                )

            Sql.addParameter command "id" clause.Id
            Sql.addParameter command "tenant_id" clause.TenantId
            Sql.addParameter command "contract_id" clause.ContractId
            Sql.addParameter command "reference" clause.Reference
            Sql.addParameter command "metric" clause.Metric
            Sql.addParameter command "measurement_window" clause.MeasurementWindow
            Sql.addParameter command "target_value" clause.TargetValue
            Sql.addParameter command "penalty_type" clause.PenaltyType
            Sql.addParameter command "penalty_config" clause.PenaltyConfigJson
            Sql.addOptionalParameter command "cap_per_period_cents" (clause.CapPerPeriodCents |> Option.map box)
            Sql.addOptionalParameter command "cap_per_contract_cents" (clause.CapPerContractCents |> Option.map box)
            Sql.addParameter command "accrual_start_from" clause.AccrualStartFrom
            Sql.addParameter command "active" clause.Active
            Sql.addParameter command "created_at" DateTimeOffset.UtcNow
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readClause reader)
            else
                return None
        }
