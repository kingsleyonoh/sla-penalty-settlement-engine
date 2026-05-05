namespace Slapen.Api.Handlers

open System
open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware
open Slapen.Audit
open Slapen.Data

[<CLIMutable>]
type CreateContractRequest =
    { CounterpartyId: Guid
      Reference: string
      Title: string
      Currency: string
      EffectiveDate: DateOnly
      ExpiryDate: Nullable<DateOnly> }

[<RequireQualifiedAccess>]
module Contracts =
    let private toDto (row: ContractRow) =
        {| id = row.Id
           counterpartyId = row.CounterpartyId
           reference = row.Reference
           title = row.Title
           currency = row.Currency
           status = row.Status |}

    let list: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! rows = ContractsRepository.list dataSource scope
                return! json {| items = rows |> List.map toDto |} next ctx
            }

    let detail contractId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! contract = ContractsRepository.findById dataSource scope contractId

                match contract with
                | None -> return! Response.notFound next ctx
                | Some row -> return! json (toDto row) next ctx
            }

    let create: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! request = ctx.BindJsonAsync<CreateContractRequest>()

                if
                    String.IsNullOrWhiteSpace request.Reference
                    || String.IsNullOrWhiteSpace request.Title
                    || String.IsNullOrWhiteSpace request.Currency
                then
                    return! Response.badRequest "reference, title, and currency are required." next ctx
                else
                    let expiry =
                        if request.ExpiryDate.HasValue then
                            Some request.ExpiryDate.Value
                        else
                            None

                    let newContract =
                        { Id = Guid.NewGuid()
                          TenantId = TenantScope.value scope
                          CounterpartyId = request.CounterpartyId
                          Reference = request.Reference.Trim()
                          Title = request.Title.Trim()
                          Source = "manual"
                          ExternalRef = None
                          Currency = request.Currency.Trim().ToUpperInvariant()
                          EffectiveDate = request.EffectiveDate
                          ExpiryDate = expiry
                          Status = "active"
                          DocumentUrl = None }

                    let! created = ContractsRepository.create dataSource scope newContract

                    match created with
                    | None -> return! Response.notFound next ctx
                    | Some row ->
                        let! _ =
                            AuditRecorder.record
                                dataSource
                                scope
                                { Actor = AuditActor.System "api"
                                  Action = "contract.created"
                                  EntityKind = "contract"
                                  EntityId = row.Id
                                  BeforeStateJson = None
                                  AfterStateJson = None }

                        return! Response.created $"/api/contracts/{row.Id}" (toDto row) next ctx
            }
