namespace Slapen.Api

open System
open Giraffe
open Slapen.Api.Handlers

[<RequireQualifiedAccess>]
module Routes =
    let app: HttpHandler =
        choose
            [ subRoute
                  "/api"
                  (choose
                      [ GET
                        >=> choose
                                [ route "/health" >=> Health.live
                                  route "/health/db" >=> Health.db
                                  route "/tenants/me" >=> Tenants.me
                                  route "/contracts" >=> Contracts.list
                                  routef "/contracts/%O" Contracts.detail
                                  routef "/contracts/%O/clauses" SlaClauses.list
                                  routef "/ledger/breaches/%O" Ledger.listByBreach
                                  route "/counterparties" >=> Counterparties.list
                                  route "/audit" >=> Audit.list ]
                        POST
                        >=> choose
                                [ route "/contracts" >=> Contracts.create
                                  routef "/contracts/%O/clauses" SlaClauses.create
                                  route "/breaches/manual" >=> Breaches.createManual
                                  routef "/breaches/%O/accrue" Breaches.accrue
                                  routef "/breaches/%O/reverse" Breaches.reverse
                                  route "/counterparties" >=> Counterparties.create ] ])
              RequestErrors.NOT_FOUND "Not Found" ]
