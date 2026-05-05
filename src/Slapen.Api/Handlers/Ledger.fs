namespace Slapen.Api.Handlers

open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module Ledger =
    let private toDto (row: LedgerEntryCandidate) =
        {| id = row.Id
           breachEventId = row.BreachEventId
           contractId = row.ContractId
           counterpartyId = row.CounterpartyId
           slaClauseId = row.SlaClauseId
           entryKind = DomainMapping.ledgerEntryKindText row.EntryKind
           direction = DomainMapping.ledgerDirectionText row.Direction
           amountCents = Money.cents row.Amount
           currency = Money.currency row.Amount
           compensatesLedgerId = row.CompensatesLedgerId
           reasonCode = DomainMapping.reasonCodeText row.ReasonCode
           createdAt = row.CreatedAt |}

    let listByBreach breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! breach = BreachEventRowsRepository.findById dataSource scope breachId

                match breach with
                | None -> return! Response.notFound next ctx
                | Some _ ->
                    let! rows = PenaltyLedgerRepository.listByBreach dataSource scope breachId
                    return! json {| items = rows |> List.map toDto |} next ctx
            }
