namespace Slapen.Api.Ui

open System
open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware
open Slapen.Application
open Slapen.Data
open Slapen.Domain
open Slapen.Templates

[<RequireQualifiedAccess>]
module UiSettlements =
    let list: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! rows = SettlementUiRepository.list dataSource scope 100

                let tableRows =
                    rows
                    |> List.map (fun row ->
                        $"""<tr><td><a href="/settlements/{row.Id}">{Html.enc row.Id}</a></td><td>{Html.enc row.CounterpartyName}</td><td>{Html.enc row.ContractReference}</td><td>{Html.money row.AmountCents row.Currency}</td><td>{Html.badge row.Status}</td><td>{Html.date row.CreatedAt}</td></tr>""")
                    |> String.concat ""

                let body =
                    Html.pageHeader "Settlements" "Credit-note approval and posting."
                    + $"""
                    <section class="action-row">
                      <form method="post" action="/settlements/build"><button class="primary" type="submit">Build settlements</button></form>
                    </section>
                    <section class="panel"><table><thead><tr><th>Settlement</th><th>Counterparty</th><th>Contract</th><th>Amount</th><th>Status</th><th>Created</th></tr></thead><tbody>{tableRows}</tbody></table></section>
                    """

                return! UiHttp.html "Settlements" "Settlements" body next ctx
            }

    let build: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let now = DateTimeOffset.UtcNow
                let periodStart = DateOnly(now.Year, now.Month, 1)
                let periodEnd = periodStart.AddMonths(1).AddDays(-1)
                let! _ = SettlementBuilder.buildPending dataSource scope periodStart periodEnd CreatedBy.System now
                return! UiHttp.redirect "/settlements" next ctx
            }

    let detail (settlementId: Guid) : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! settlement = SettlementsRepository.findById dataSource scope settlementId

                match settlement with
                | None -> return! RequestErrors.NOT_FOUND "Not Found" next ctx
                | Some row ->
                    let body =
                        Html.pageHeader $"Settlement {row.Id}" $"Status: {row.Status}"
                        + $"""
                        <section class="action-row">
                          <form method="post" action="/settlements/{row.Id}/approve"><button class="primary" type="submit">Approve</button></form>
                          <form method="post" action="/settlements/{row.Id}/post"><button type="submit">Post local PDF</button></form>
                          <a class="button-link" href="/settlements/{row.Id}/preview">PDF preview</a>
                          <a class="button-link" href="/settlements/{row.Id}/download">Download PDF</a>
                        </section>
                        <section class="panel"><dl>
                          <dt>Amount</dt><dd>{Html.money row.AmountCents row.Currency}</dd>
                          <dt>PDF URL</dt><dd>{Html.enc (row.PdfUrl |> Option.defaultValue "")}</dd>
                        </dl></section>
                        """

                    return! UiHttp.html $"Settlement {row.Id}" "Settlements" body next ctx
            }

    let approve (settlementId: Guid) : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! _ = SettlementUiRepository.approve dataSource scope settlementId DateTimeOffset.UtcNow
                return! UiHttp.redirect $"/settlements/{settlementId}" next ctx
            }

    let post (settlementId: Guid) : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx

                let! _ =
                    SettlementPoster.postReadySettlement
                        dataSource
                        scope
                        { InvoiceReconEnabled = false
                          LocalPdfDirectory = None }
                        settlementId
                        DateTimeOffset.UtcNow

                return! UiHttp.redirect $"/settlements/{settlementId}" next ctx
            }

    let private pdfResponse download (settlementId: Guid) : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! settlement = SettlementsRepository.findById dataSource scope settlementId

                match settlement |> Option.bind _.PdfSnapshotJson with
                | None -> return! RequestErrors.NOT_FOUND "Not Found" next ctx
                | Some snapshotJson ->
                    let bytes =
                        snapshotJson
                        |> SettlementPdf.deserializeSnapshot
                        |> PdfRenderer.renderSettlement

                    ctx.Response.ContentType <- "application/pdf"

                    if download then
                        ctx.Response.Headers.ContentDisposition <-
                            $"attachment; filename=\"settlement-{settlementId}.pdf\""
                    else
                        ctx.Response.Headers.ContentDisposition <- "inline"

                    return! ctx.WriteBytesAsync bytes
            }

    let preview settlementId = pdfResponse false settlementId

    let download settlementId = pdfResponse true settlementId
