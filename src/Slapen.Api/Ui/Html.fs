namespace Slapen.Api.Ui

open System
open System.Net
open Slapen.Api.Middleware

[<RequireQualifiedAccess>]
module Html =
    let enc (value: obj) =
        if isNull value then
            ""
        else
            WebUtility.HtmlEncode(string value)

    let money (cents: int64) currency =
        let major = decimal cents / 100M
        $"{major:N2} {currency}"

    let date (value: DateTimeOffset) = value.ToString("yyyy-MM-dd HH:mm")

    let layout (tenant: TenantContext option) (active: string) (title: string) (body: string) =
        let tenantName =
            tenant
            |> Option.map (fun context -> enc context.Tenant.DisplayName)
            |> Option.defaultValue ""

        let navItem path label =
            let activeClass = if active = label then " nav-active" else ""

            $"""<a class="nav-link{activeClass}" href="{path}">{label}</a>"""

        let nav =
            match tenant with
            | None -> ""
            | Some _ ->
                $"""
                <aside class="sidebar">
                  <div class="brand">SLA Settlement</div>
                  <div class="tenant" data-testid="tenant-display-name">{tenantName}</div>
                  <nav>
                    {navItem "/" "Dashboard"}
                    {navItem "/breaches" "Breaches"}
                    {navItem "/contracts" "Contracts"}
                    {navItem "/counterparties" "Counterparties"}
                    {navItem "/ledger" "Ledger"}
                    {navItem "/settlements" "Settlements"}
                    {navItem "/settings/tenant" "Settings"}
                    {navItem "/settings/ingestion" "Ingestion"}
                  </nav>
                  <form method="post" action="/logout"><button class="nav-link button-link" type="submit">Sign out</button></form>
                </aside>
                """

        $"""<!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>{enc title}</title>
          <link rel="stylesheet" href="/ui.css" />
        </head>
        <body>
          {nav}
          <main class="main">
            {body}
          </main>
        </body>
        </html>"""

    let pageHeader title subtitle =
        $"""<header class="page-header"><div><h1>{enc title}</h1><p>{enc subtitle}</p></div></header>"""

    let badge value =
        $"""<span class="badge">{enc value}</span>"""
