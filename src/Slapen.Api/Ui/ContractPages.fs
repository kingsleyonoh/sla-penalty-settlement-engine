namespace Slapen.Api.Ui

open System
open Giraffe
open Npgsql
open Slapen.Api.Middleware
open Slapen.Data

[<RequireQualifiedAccess>]
module UiContracts =
    let contractDetail contractId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! contract = ContractsRepository.findById dataSource scope contractId

                match contract with
                | None -> return! RequestErrors.NOT_FOUND "Not Found" next ctx
                | Some contract ->
                    let! clauses = SlaClausesRepository.listByContract dataSource scope contract.Id

                    let rows =
                        defaultArg clauses []
                        |> List.map (fun row ->
                            $"""<tr><td>{Html.enc row.Reference}</td><td>{Html.enc row.Metric}</td><td>{Html.enc row.PenaltyType}</td><td>{Html.badge (string row.Active)}</td></tr>""")
                        |> String.concat ""

                    let body =
                        Html.pageHeader contract.Reference contract.Title
                        + $"""
                        <section class="form-panel"><form method="post" action="/contracts/{contract.Id}/clauses" class="inline-form">
                          <label for="clauseReference">Clause reference</label><input id="clauseReference" name="reference" required />
                          <label for="metric">Metric</label><input id="metric" name="metric" value="uptime_percent" required />
                          <label for="targetValue">Target value</label><input id="targetValue" name="targetValue" value="99.9" required />
                          <label for="amountCents">Flat amount cents</label><input id="amountCents" name="amountCents" value="50000" required />
                          <button class="primary" type="submit">Add clause</button>
                        </form></section>
                        <section class="panel"><table><thead><tr><th>Clause</th><th>Metric</th><th>Penalty</th><th>Active</th></tr></thead><tbody>{rows}</tbody></table></section>
                        """

                    return! UiHttp.html contract.Reference "Contracts" body next ctx
            }

    let createClause contractId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let tenant = TenantAuth.requireTenant ctx
                let! form = ctx.Request.ReadFormAsync()
                let amount = Int64.Parse(UiHttp.formValue form "amountCents")

                let row =
                    { Id = Guid.NewGuid()
                      TenantId = TenantScope.value scope
                      ContractId = contractId
                      Reference = (UiHttp.formValue form "reference").Trim()
                      Metric = (UiHttp.formValue form "metric").Trim()
                      MeasurementWindow = "per_incident"
                      TargetValue = Decimal.Parse(UiHttp.formValue form "targetValue")
                      PenaltyType = "flat_per_breach"
                      PenaltyConfigJson =
                        $"""{{"amount_cents":{amount},"currency":"{tenant.Tenant.DefaultCurrency}"}}"""
                      CapPerPeriodCents = None
                      CapPerContractCents = None
                      AccrualStartFrom = "breach_observed_at"
                      Active = true }

                let! _ = SlaClausesRepository.create dataSource scope row
                return! UiHttp.redirect $"/contracts/{contractId}" next ctx
            }
