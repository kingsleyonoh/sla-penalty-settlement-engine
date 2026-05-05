namespace Slapen.Api.Ui

open System
open Giraffe
open Npgsql
open Slapen.Api.Middleware
open Slapen.Data

[<RequireQualifiedAccess>]
module UiDirectories =
    let counterparties: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! rows = CounterpartiesRepository.list dataSource scope

                let items =
                    rows
                    |> List.map (fun row ->
                        $"""<tr><td>{Html.enc row.CanonicalName}</td><td>{Html.enc (defaultArg row.CountryCode "")}</td><td>{Html.enc (defaultArg row.DefaultCurrency "")}</td></tr>""")
                    |> String.concat ""

                let body =
                    Html.pageHeader "Counterparties" "Supplier directory scoped to the signed-in tenant."
                    + $"""
                    <section class="form-panel">
                      <form method="post" action="/counterparties" class="inline-form">
                        <label for="canonicalName">Counterparty name</label><input id="canonicalName" name="canonicalName" required />
                        <label for="countryCode">Country code</label><input id="countryCode" name="countryCode" maxlength="2" />
                        <label for="defaultCurrency">Default currency</label><input id="defaultCurrency" name="defaultCurrency" maxlength="3" />
                        <button class="primary" type="submit">Add counterparty</button>
                      </form>
                    </section>
                    <section class="panel"><table><thead><tr><th>Name</th><th>Country</th><th>Currency</th></tr></thead><tbody>{items}</tbody></table></section>
                    """

                return! UiHttp.html "Counterparties" "Counterparties" body next ctx
            }

    let createCounterparty: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! form = ctx.Request.ReadFormAsync()
                let name = UiHttp.formValue form "canonicalName"

                if String.IsNullOrWhiteSpace name then
                    return! UiHttp.redirect "/counterparties" next ctx
                else
                    let row =
                        { Id = Guid.NewGuid()
                          TenantId = TenantScope.value scope
                          CanonicalName = name.Trim()
                          TaxId = None
                          CountryCode = UiHttp.optionValue (UiHttp.formValue form "countryCode")
                          ExternalRefsJson = "{}"
                          DefaultCurrency = UiHttp.optionValue (UiHttp.formValue form "defaultCurrency") }

                    let! _ = CounterpartiesRepository.create dataSource scope row
                    return! UiHttp.redirect "/counterparties" next ctx
            }

    let contracts: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! contracts = ContractsRepository.list dataSource scope
                let! counterparties = CounterpartiesRepository.list dataSource scope

                let counterpartyOptions =
                    counterparties
                    |> List.map (fun row -> $"""<option value="{row.Id}">{Html.enc row.CanonicalName}</option>""")
                    |> String.concat ""

                let rows =
                    contracts
                    |> List.map (fun row ->
                        $"""<tr><td><a href="/contracts/{row.Id}">{Html.enc row.Reference}</a></td><td>{Html.enc row.Title}</td><td>{Html.badge row.Status}</td></tr>""")
                    |> String.concat ""

                let body =
                    Html.pageHeader "Contracts" "Manual contracts and clause setup."
                    + $"""
                    <section class="form-panel"><form method="post" action="/contracts" class="inline-form">
                      <label for="counterpartyId">Counterparty</label><select id="counterpartyId" name="counterpartyId">{counterpartyOptions}</select>
                      <label for="reference">Reference</label><input id="reference" name="reference" required />
                      <label for="title">Title</label><input id="title" name="title" required />
                      <button class="primary" type="submit">Add contract</button>
                    </form></section>
                    <section class="panel"><table><thead><tr><th>Reference</th><th>Title</th><th>Status</th></tr></thead><tbody>{rows}</tbody></table></section>
                    """

                return! UiHttp.html "Contracts" "Contracts" body next ctx
            }

    let createContract: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let tenant = TenantAuth.requireTenant ctx
                let! form = ctx.Request.ReadFormAsync()

                let row =
                    { Id = Guid.NewGuid()
                      TenantId = TenantScope.value scope
                      CounterpartyId = Guid.Parse(UiHttp.formValue form "counterpartyId")
                      Reference = (UiHttp.formValue form "reference").Trim()
                      Title = (UiHttp.formValue form "title").Trim()
                      Source = "manual"
                      ExternalRef = None
                      Currency = tenant.Tenant.DefaultCurrency
                      EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.Date)
                      ExpiryDate = None
                      Status = "active"
                      DocumentUrl = None }

                let! _ = ContractsRepository.create dataSource scope row
                return! UiHttp.redirect "/contracts" next ctx
            }
