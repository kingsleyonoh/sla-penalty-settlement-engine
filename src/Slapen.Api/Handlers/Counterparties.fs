namespace Slapen.Api.Handlers

open System
open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware
open Slapen.Audit
open Slapen.Data

[<CLIMutable>]
type CreateCounterpartyRequest =
    { CanonicalName: string
      TaxId: string
      CountryCode: string
      DefaultCurrency: string }

[<RequireQualifiedAccess>]
module Counterparties =
    let private toDto (row: CounterpartyRow) =
        {| id = row.Id
           canonicalName = row.CanonicalName
           taxId = row.TaxId
           countryCode = row.CountryCode
           defaultCurrency = row.DefaultCurrency |}

    let list: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! rows = CounterpartiesRepository.list dataSource scope
                return! json {| items = rows |> List.map toDto |} next ctx
            }

    let create: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! request = ctx.BindJsonAsync<CreateCounterpartyRequest>()

                if String.IsNullOrWhiteSpace request.CanonicalName then
                    return! Response.badRequest "canonicalName is required." next ctx
                else
                    let tenantId = TenantScope.value scope

                    let counterparty =
                        { Id = Guid.NewGuid()
                          TenantId = tenantId
                          CanonicalName = request.CanonicalName.Trim()
                          TaxId = Option.ofObj request.TaxId
                          CountryCode = Option.ofObj request.CountryCode
                          ExternalRefsJson = "{}"
                          DefaultCurrency = Option.ofObj request.DefaultCurrency }

                    let! created = CounterpartiesRepository.create dataSource scope counterparty

                    let! _ =
                        AuditRecorder.record
                            dataSource
                            scope
                            { Actor = AuditActor.System "api"
                              Action = "counterparty.created"
                              EntityKind = "counterparty"
                              EntityId = created.Id
                              BeforeStateJson = None
                              AfterStateJson = None }

                    return! Response.created $"/api/counterparties/{created.Id}" (toDto created) next ctx
            }
