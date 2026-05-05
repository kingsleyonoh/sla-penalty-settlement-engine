namespace Slapen.Api.Handlers

open System
open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware
open Slapen.Application
open Slapen.Audit
open Slapen.Data
open Slapen.Domain

[<CLIMutable>]
type CreateManualBreachRequest =
    { ContractId: Guid
      SlaClauseId: Guid
      SourceRef: string
      MetricValue: decimal
      UnitsMissed: Nullable<decimal>
      ObservedAt: DateTimeOffset
      ReportedAt: DateTimeOffset }

[<CLIMutable>]
type ReverseBreachRequest = { ReasonNotes: string }

[<RequireQualifiedAccess>]
module Breaches =
    let private breachDto (row: BreachEventRow) =
        {| id = row.Id
           contractId = row.ContractId
           slaClauseId = row.SlaClauseId
           source = row.Source
           sourceRef = row.SourceRef
           metricValue = row.MetricValue
           unitsMissed = row.UnitsMissed
           observedAt = row.ObservedAt
           reportedAt = row.ReportedAt
           status = row.Status |}

    let createManual: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! request = ctx.BindJsonAsync<CreateManualBreachRequest>()

                let unitsMissed =
                    if request.UnitsMissed.HasValue then
                        Some request.UnitsMissed.Value
                    else
                        None

                let! result =
                    Ingestion.ingestManual
                        dataSource
                        scope
                        { ContractId = request.ContractId
                          SlaClauseId = request.SlaClauseId
                          SourceRef = Option.ofObj request.SourceRef
                          MetricValue = request.MetricValue
                          UnitsMissed = unitsMissed
                          ObservedAt = request.ObservedAt
                          ReportedAt = request.ReportedAt
                          RawPayloadJson = """{"source":"api"}""" }

                match result.BreachIds with
                | [] -> return! Response.notFound next ctx
                | breachId :: _ ->
                    let! row = BreachEventRowsRepository.findById dataSource scope breachId

                    let row =
                        row |> Option.defaultWith (fun _ -> failwith "Created breach was not readable.")

                    let! _ =
                        AuditRecorder.record
                            dataSource
                            scope
                            { Actor = AuditActor.System "api"
                              Action = "breach.created"
                              EntityKind = "breach_event"
                              EntityId = row.Id
                              BeforeStateJson = None
                              AfterStateJson = None }

                    return! Response.created $"/api/breaches/{row.Id}" (breachDto row) next ctx
            }

    let accrue breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! outcome = AccrualWorker.processBreach dataSource scope breachId DateTimeOffset.UtcNow

                match outcome with
                | AccrualWorker.Outcome.NotFound -> return! Response.notFound next ctx
                | AccrualWorker.Outcome.NotAccruable _ ->
                    return! Response.conflict "Breach cannot be accrued from its current status." next ctx
                | AccrualWorker.Outcome.NoPenalty reason ->
                    return!
                        json
                            {| status = "no_penalty"
                               reason = string reason |}
                            next
                            ctx
                | AccrualWorker.Outcome.CalculationFailed error ->
                    return! Response.badRequest $"Penalty calculation failed: {error}" next ctx
                | AccrualWorker.Outcome.Accrued ids ->
                    let! _ =
                        AuditRecorder.record
                            dataSource
                            scope
                            { Actor = AuditActor.System "api"
                              Action = "breach.accrued"
                              EntityKind = "breach_event"
                              EntityId = breachId
                              BeforeStateJson = None
                              AfterStateJson = None }

                    return!
                        json
                            {| status = "accrued"
                               ledgerEntryIds = ids |}
                            next
                            ctx
            }

    let reverse breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! request = ctx.BindJsonAsync<ReverseBreachRequest>()

                let notes =
                    if String.IsNullOrWhiteSpace request.ReasonNotes then
                        None
                    else
                        Some request.ReasonNotes

                let! outcome =
                    ReversalEngine.reverseBreach
                        dataSource
                        scope
                        breachId
                        BreachStatus.Withdrawn
                        ReasonCode.WithdrawnBySource
                        notes
                        CreatedBy.System
                        DateTimeOffset.UtcNow

                match outcome with
                | ReversalEngine.Outcome.NotFound -> return! Response.notFound next ctx
                | ReversalEngine.Outcome.NoAccrualsToReverse ->
                    return! Response.conflict "Breach has no accruals to reverse." next ctx
                | ReversalEngine.Outcome.InvalidTransition _ ->
                    return! Response.conflict "Breach cannot be reversed from its current status." next ctx
                | ReversalEngine.Outcome.WriteFailed error ->
                    return! Response.badRequest $"Reversal failed: {error}" next ctx
                | ReversalEngine.Outcome.Reversed ids ->
                    let! _ =
                        AuditRecorder.record
                            dataSource
                            scope
                            { Actor = AuditActor.System "api"
                              Action = "breach.reversed"
                              EntityKind = "breach_event"
                              EntityId = breachId
                              BeforeStateJson = None
                              AfterStateJson = None }

                    return!
                        json
                            {| status = "withdrawn"
                               ledgerEntryIds = ids |}
                            next
                            ctx
            }
