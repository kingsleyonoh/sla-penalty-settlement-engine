namespace Slapen.Api

open System
open Giraffe
open Slapen.Api.Handlers
open Slapen.Api.Ui

[<RequireQualifiedAccess>]
module Routes =
    let app: HttpHandler =
        choose
            [ GET
              >=> choose
                      [ route "/login" >=> UiAuth.login
                        route "/" >=> UiDashboard.dashboard
                        route "/breaches" >=> UiBreachLedger.breaches
                        routef "/breaches/%O" UiBreachLedger.breachDetail
                        route "/contracts" >=> UiDirectories.contracts
                        routef "/contracts/%O" UiContracts.contractDetail
                        route "/counterparties" >=> UiDirectories.counterparties
                        route "/ledger" >=> UiBreachLedger.ledger
                        route "/settings/tenant" >=> UiDashboard.settings ]
              POST
              >=> choose
                      [ route "/login" >=> UiAuth.loginPost
                        route "/logout" >=> UiAuth.logout
                        route "/breaches" >=> UiBreachLedger.createBreach
                        routef "/breaches/%O/accrue" UiBreachLedger.accrue
                        routef "/breaches/%O/reverse" UiBreachLedger.reverse
                        route "/contracts" >=> UiDirectories.createContract
                        routef "/contracts/%O/clauses" UiContracts.createClause
                        route "/counterparties" >=> UiDirectories.createCounterparty ]
              subRoute
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
