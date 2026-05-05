namespace Slapen.Api.Ui

open Giraffe
open Npgsql
open Slapen.Api.Middleware
open Slapen.Application
open Slapen.Data

[<RequireQualifiedAccess>]
module UiIngestionSettings =
    let private statusText (setting: IngestionAdapterSetting) =
        match setting.LastTestStatus with
        | Some status -> status
        | None -> "not tested"

    let private pullText (setting: IngestionAdapterSetting) =
        match setting.LastPullRequestedAt with
        | Some _ -> "Pull requested"
        | None -> "No pull requested"

    let private row (setting: IngestionAdapterSetting) =
        let enabledBadge = if setting.Enabled then "Enabled" else "Disabled"
        let toggleAction = if setting.Enabled then "disable" else "enable"
        let toggleLabel = if setting.Enabled then "Disable" else "Enable"

        $"""
        <tr>
          <td><strong>{Html.enc setting.DisplayName}</strong><span class="muted-code">{Html.enc setting.Adapter}</span></td>
          <td>{Html.badge enabledBadge}</td>
          <td>{Html.enc (statusText setting)}</td>
          <td>{Html.enc (pullText setting)}</td>
          <td>
            <div class="action-row compact">
              <form method="post" action="/settings/ingestion/{setting.Adapter}/{toggleAction}"><button type="submit">{toggleLabel}</button></form>
              <form method="post" action="/settings/ingestion/{setting.Adapter}/test"><button type="submit">Test</button></form>
              <form method="post" action="/settings/ingestion/{setting.Adapter}/pull-now"><button class="primary" type="submit">Pull now</button></form>
            </div>
          </td>
        </tr>
        """

    let page: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! settings = IngestionSettingsRepository.list dataSource scope

                let rows = settings |> List.map row |> String.concat ""

                let body =
                    Html.pageHeader "Ingestion settings" "Tenant-scoped adapter controls."
                    + $"""
                    <section class="panel">
                      <table>
                        <thead><tr><th>Adapter</th><th>State</th><th>Last test</th><th>Pull</th><th>Actions</th></tr></thead>
                        <tbody>{rows}</tbody>
                      </table>
                    </section>
                    """

                return! UiHttp.html "Ingestion settings" "Settings" body next ctx
            }

    let action adapter actionName : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let now = System.DateTimeOffset.UtcNow

                match actionName with
                | "enable" ->
                    let! _ = IngestionSettingsRepository.setEnabled dataSource scope adapter true now
                    return! UiHttp.redirect "/settings/ingestion" next ctx
                | "disable" ->
                    let! _ = IngestionSettingsRepository.setEnabled dataSource scope adapter false now
                    return! UiHttp.redirect "/settings/ingestion" next ctx
                | "test" ->
                    let! _ = IngestionControl.testAdapter dataSource scope adapter now
                    return! UiHttp.redirect "/settings/ingestion" next ctx
                | "pull-now" ->
                    let! _ = IngestionControl.requestPullNow dataSource scope adapter now
                    return! UiHttp.redirect "/settings/ingestion" next ctx
                | _ -> return! RequestErrors.NOT_FOUND "Not Found" next ctx
            }
