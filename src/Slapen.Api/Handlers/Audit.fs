namespace Slapen.Api.Handlers

open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware
open Slapen.Data

[<RequireQualifiedAccess>]
module Audit =
    let private toDto (row: AuditLogRow) =
        {| id = row.Id
           actorKind = row.ActorKind
           actorId = row.ActorId
           action = row.Action
           entityKind = row.EntityKind
           entityId = row.EntityId
           occurredAt = row.OccurredAt |}

    let list: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx

                let limit =
                    match ctx.TryGetQueryStringValue "limit" with
                    | Some value ->
                        match System.Int32.TryParse value with
                        | true, parsed -> Response.clampLimit parsed
                        | _ -> 50
                    | None -> 50

                let! rows = AuditRepository.listForTenant dataSource scope limit
                return! json {| items = rows |> List.map toDto |} next ctx
            }
