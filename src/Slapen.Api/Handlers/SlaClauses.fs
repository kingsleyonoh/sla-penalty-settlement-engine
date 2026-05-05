namespace Slapen.Api.Handlers

open System
open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware
open Slapen.Audit
open Slapen.Data

[<CLIMutable>]
type CreateSlaClauseRequest =
    { Reference: string
      Metric: string
      MeasurementWindow: string
      TargetValue: decimal
      PenaltyType: string
      PenaltyConfigJson: string
      CapPerPeriodCents: Nullable<int64>
      CapPerContractCents: Nullable<int64> }

[<RequireQualifiedAccess>]
module SlaClauses =
    let private toDto (row: SlaClauseRow) =
        {| id = row.Id
           contractId = row.ContractId
           reference = row.Reference
           metric = row.Metric
           measurementWindow = row.MeasurementWindow
           targetValue = row.TargetValue
           penaltyType = row.PenaltyType
           penaltyConfig = row.PenaltyConfigJson
           active = row.Active |}

    let list contractId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! rows = SlaClausesRepository.listByContract dataSource scope contractId

                match rows with
                | None -> return! Response.notFound next ctx
                | Some clauses -> return! json {| items = clauses |> List.map toDto |} next ctx
            }

    let create contractId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! request = ctx.BindJsonAsync<CreateSlaClauseRequest>()

                if
                    String.IsNullOrWhiteSpace request.Reference
                    || String.IsNullOrWhiteSpace request.Metric
                    || String.IsNullOrWhiteSpace request.PenaltyType
                    || String.IsNullOrWhiteSpace request.PenaltyConfigJson
                then
                    return!
                        Response.badRequest
                            "reference, metric, penaltyType, and penaltyConfigJson are required."
                            next
                            ctx
                else
                    let capPerPeriod =
                        if request.CapPerPeriodCents.HasValue then
                            Some request.CapPerPeriodCents.Value
                        else
                            None

                    let capPerContract =
                        if request.CapPerContractCents.HasValue then
                            Some request.CapPerContractCents.Value
                        else
                            None

                    let clause =
                        { Id = Guid.NewGuid()
                          TenantId = TenantScope.value scope
                          ContractId = contractId
                          Reference = request.Reference.Trim()
                          Metric = request.Metric.Trim()
                          MeasurementWindow = request.MeasurementWindow
                          TargetValue = request.TargetValue
                          PenaltyType = request.PenaltyType
                          PenaltyConfigJson = request.PenaltyConfigJson
                          CapPerPeriodCents = capPerPeriod
                          CapPerContractCents = capPerContract
                          AccrualStartFrom = "breach_observed_at"
                          Active = true }

                    let! created = SlaClausesRepository.create dataSource scope clause

                    match created with
                    | None -> return! Response.notFound next ctx
                    | Some row ->
                        let! _ =
                            AuditRecorder.record
                                dataSource
                                scope
                                { Actor = AuditActor.System "api"
                                  Action = "sla_clause.created"
                                  EntityKind = "sla_clause"
                                  EntityId = row.Id
                                  BeforeStateJson = None
                                  AfterStateJson = None }

                        return! Response.created $"/api/contracts/{contractId}/clauses/{row.Id}" (toDto row) next ctx
            }
