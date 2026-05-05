namespace Slapen.Api.Ui

open Giraffe
open Npgsql
open Slapen.Api.Middleware
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module UiDashboard =
    let dashboard: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let tenant = TenantAuth.requireTenant ctx
                let! summary = DashboardRepository.summary dataSource scope 50

                let breachRows =
                    summary.RecentPending
                    |> List.map (fun breach ->
                        $"""<tr><td>{Html.enc breach.Id}</td><td>{Html.badge breach.Status}</td><td>{Html.date breach.ObservedAt}</td></tr>""")
                    |> String.concat ""

                let body =
                    Html.pageHeader "Dashboard" $"Operational view for {tenant.Tenant.DisplayName}"
                    + $"""
                    <section class="metric-grid">
                      <div class="metric"><span>Pending breaches</span><strong>{summary.PendingBreaches}</strong></div>
                      <div class="metric"><span>Active contracts</span><strong>{summary.ActiveContracts}</strong></div>
                      <div class="metric"><span>Ledger movement</span><strong>{Html.money summary.LedgerMovementCents tenant.Tenant.DefaultCurrency}</strong></div>
                    </section>
                    <section class="panel">
                      <h2>Breaches awaiting attention</h2>
                      <table><tbody>{breachRows}</tbody></table>
                    </section>
                    """

                return! UiHttp.html "Dashboard" "Dashboard" body next ctx
            }

    let settings: HttpHandler =
        fun next ctx ->
            task {
                let tenant = TenantAuth.requireTenant ctx

                let body =
                    Html.pageHeader "Tenant and API key settings" "Tenant identity is resolved from the active session."
                    + $"""
                    <section class="panel settings-grid">
                      <div><span>Name</span><strong>{Html.enc tenant.Tenant.Name}</strong></div>
                      <div><span>Display name</span><strong>{Html.enc tenant.Tenant.DisplayName}</strong></div>
                      <div><span>Locale</span><strong>{Html.enc tenant.Tenant.Locale}</strong></div>
                      <div><span>Timezone</span><strong>{Html.enc tenant.Tenant.Timezone}</strong></div>
                      <div><span>Default currency</span><strong>{Html.enc tenant.Tenant.DefaultCurrency}</strong></div>
                      <div><span>API key</span><strong>Stored hashed; raw value is accepted only at sign in or rotation.</strong></div>
                    </section>
                    """

                return! UiHttp.html "Settings" "Settings" body next ctx
            }
