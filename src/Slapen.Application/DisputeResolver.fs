namespace Slapen.Application

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module DisputeResolver =
    type Resolution =
        | ResolvedInOurFavor
        | ResolvedAgainstUs

    type Outcome =
        | DisputeOpened
        | DisputeResolved of ReversalIds: Guid list
        | InvalidTransition of fromStatus: BreachStatus
        | ReversalFailed of ReversalEngine.Outcome
        | NotFound

    let private invalidOrNotFound dataSource scope breachEventId =
        task {
            let! status = DisputeRepository.status dataSource scope breachEventId

            match status with
            | None -> return NotFound
            | Some status -> return InvalidTransition status
        }

    let openDispute
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        (reason: string)
        (userId: Guid option)
        (disputedAt: DateTimeOffset)
        : Task<Outcome> =
        task {
            let! opened = DisputeRepository.openDispute dataSource scope breachEventId reason userId disputedAt

            if opened then
                return DisputeOpened
            else
                return! invalidOrNotFound dataSource scope breachEventId
        }

    let resolveDispute
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        (resolution: Resolution)
        (notes: string)
        (createdBy: CreatedBy)
        (resolvedAt: DateTimeOffset)
        : Task<Outcome> =
        task {
            match resolution with
            | ResolvedInOurFavor ->
                let! resolved = DisputeRepository.resolveInOurFavor dataSource scope breachEventId resolvedAt

                if resolved then
                    return DisputeResolved []
                else
                    return! invalidOrNotFound dataSource scope breachEventId
            | ResolvedAgainstUs ->
                let! reversed =
                    ReversalEngine.reverseBreach
                        dataSource
                        scope
                        breachEventId
                        BreachStatus.Withdrawn
                        ReasonCode.DisputeResolvedAgainst
                        (Some notes)
                        createdBy
                        resolvedAt

                match reversed with
                | ReversalEngine.Reversed ids -> return DisputeResolved ids
                | ReversalEngine.NotFound -> return NotFound
                | other -> return ReversalFailed other
        }
