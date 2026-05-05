namespace Slapen.Api.Ui

open System
open System.IO
open Giraffe
open Npgsql
open Slapen.Api.Middleware
open Slapen.Application
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module UiBreachLedger =
    let private allClauseOptions dataSource scope =
        task {
            let! contracts = ContractsRepository.list dataSource scope
            let options = ResizeArray<string>()

            for contract in contracts do
                let! clauses = SlaClausesRepository.listByContract dataSource scope contract.Id

                for clause in defaultArg clauses [] do
                    options.Add(
                        $"""<option value="{contract.Id}|{clause.Id}">{Html.enc contract.Reference} - {Html.enc clause.Reference}</option>"""
                    )

            return String.concat "" options
        }

    let breaches: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! clauseOptions = allClauseOptions dataSource scope
                let! rows = BreachEventRowsRepository.list dataSource scope 50

                let breachRows =
                    rows
                    |> List.map (fun row ->
                        $"""<tr><td><a href="/breaches/{row.Id}">{Html.enc row.Id}</a></td><td>{Html.badge row.Status}</td><td>{Html.date row.ObservedAt}</td></tr>""")
                    |> String.concat ""

                let body =
                    Html.pageHeader "Breaches" "Manual breach queue and actions."
                    + $"""
                    <section class="form-panel"><form method="post" action="/breaches" class="inline-form">
                      <label for="clauseSelector">Clause</label><select id="clauseSelector" name="clauseSelector">{clauseOptions}</select>
                      <label for="metricValue">Metric value</label><input id="metricValue" name="metricValue" required />
                      <button class="primary" type="submit">Record breach</button>
                    </form></section>
                    <section class="form-panel"><form method="post" action="/breaches/csv" enctype="multipart/form-data" class="inline-form">
                      <label for="breachCsv">CSV breach file</label><input id="breachCsv" name="breachCsv" type="file" accept=".csv,text/csv" required />
                      <button type="submit">Upload CSV</button>
                    </form></section>
                    <section class="panel"><table><thead><tr><th>Breach</th><th>Status</th><th>Observed</th></tr></thead><tbody>{breachRows}</tbody></table></section>
                    """

                return! UiHttp.html "Breaches" "Breaches" body next ctx
            }

    let createBreach: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! form = ctx.Request.ReadFormAsync()

                let parts =
                    (UiHttp.formValue form "clauseSelector").Split('|', StringSplitOptions.RemoveEmptyEntries)

                let contractId = Guid.Parse parts[0]
                let clauseId = Guid.Parse parts[1]
                let now = DateTimeOffset.UtcNow

                let! created =
                    Ingestion.ingestManual
                        dataSource
                        scope
                        { ContractId = contractId
                          SlaClauseId = clauseId
                          SourceRef = Some $"ui-{Guid.NewGuid():N}"
                          MetricValue = Decimal.Parse(UiHttp.formValue form "metricValue")
                          UnitsMissed = None
                          ObservedAt = now
                          ReportedAt = now
                          RawPayloadJson = """{"source":"ui"}""" }

                match created.BreachIds with
                | breachId :: _ -> return! UiHttp.redirect $"/breaches/{breachId}" next ctx
                | [] -> return! UiHttp.redirect "/breaches" next ctx
            }

    let uploadCsv: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! form = ctx.Request.ReadFormAsync()
                let file = form.Files.GetFile "breachCsv"

                if isNull file then
                    return! UiHttp.redirect "/breaches" next ctx
                else
                    use stream = file.OpenReadStream()
                    use reader = new StreamReader(stream)
                    let! csv = reader.ReadToEndAsync()
                    let! _ = Ingestion.ingestCsv dataSource scope csv
                    return! UiHttp.redirect "/breaches" next ctx
            }

    let breachDetail breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! breach = BreachEventRowsRepository.findById dataSource scope breachId

                match breach with
                | None -> return! RequestErrors.NOT_FOUND "Not Found" next ctx
                | Some breach ->
                    let! ledger = PenaltyLedgerRepository.listByBreach dataSource scope breach.Id

                    let timeline =
                        ledger
                        |> List.map (fun row ->
                            $"""<tr><td>{Html.enc (DomainMapping.ledgerEntryKindText row.EntryKind)}</td><td>{Html.enc (DomainMapping.ledgerDirectionText row.Direction)}</td><td>{Html.money (Money.cents row.Amount) (Money.currency row.Amount)}</td><td>{Html.enc (DomainMapping.reasonCodeText row.ReasonCode)}</td></tr>""")
                        |> String.concat ""

                    let body =
                        Html.pageHeader $"Breach {breach.Id}" $"Status: {breach.Status}"
                        + $"""
                        <section class="action-row">
                          <form method="post" action="/breaches/{breach.Id}/accrue"><button class="primary" type="submit">Accrue</button></form>
                          <form method="post" action="/breaches/{breach.Id}/reverse"><button type="submit">Reverse</button></form>
                          <form method="post" action="/breaches/{breach.Id}/dispute"><input name="reason" value="Counterparty disputed breach" hidden /><button type="submit">Open dispute</button></form>
                          <form method="post" action="/breaches/{breach.Id}/resolve-our-favor"><button type="submit">Resolve in our favor</button></form>
                          <form method="post" action="/breaches/{breach.Id}/resolve-against-us"><button type="submit">Resolve against us</button></form>
                        </section>
                        <section class="panel"><h2>Ledger timeline</h2><table><thead><tr><th>Kind</th><th>Direction</th><th>Amount</th><th>Reason</th></tr></thead><tbody>{timeline}</tbody></table></section>
                        """

                    return! UiHttp.html $"Breach {breach.Id}" "Breaches" body next ctx
            }

    let accrue breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! _ = AccrualWorker.processBreach dataSource scope breachId DateTimeOffset.UtcNow
                return! UiHttp.redirect $"/breaches/{breachId}" next ctx
            }

    let dispute breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! form = ctx.Request.ReadFormAsync()
                let reason = UiHttp.formValue form "reason"

                let reasonText =
                    if String.IsNullOrWhiteSpace reason then
                        "Counterparty disputed breach"
                    else
                        reason

                let! _ = DisputeResolver.openDispute dataSource scope breachId reasonText None DateTimeOffset.UtcNow
                return! UiHttp.redirect $"/breaches/{breachId}" next ctx
            }

    let resolveOurFavor breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx

                let! _ =
                    DisputeResolver.resolveDispute
                        dataSource
                        scope
                        breachId
                        DisputeResolver.ResolvedInOurFavor
                        "Resolved in our favor from UI"
                        CreatedBy.System
                        DateTimeOffset.UtcNow

                return! UiHttp.redirect $"/breaches/{breachId}" next ctx
            }

    let resolveAgainstUs breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx

                let! _ =
                    DisputeResolver.resolveDispute
                        dataSource
                        scope
                        breachId
                        DisputeResolver.ResolvedAgainstUs
                        "Resolved against us from UI"
                        CreatedBy.System
                        DateTimeOffset.UtcNow

                return! UiHttp.redirect $"/breaches/{breachId}" next ctx
            }

    let reverse breachId : HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx

                let! _ =
                    ReversalEngine.reverseBreach
                        dataSource
                        scope
                        breachId
                        BreachStatus.Withdrawn
                        ReasonCode.WithdrawnBySource
                        (Some "withdrawn from UI")
                        CreatedBy.System
                        DateTimeOffset.UtcNow

                return! UiHttp.redirect $"/breaches/{breachId}" next ctx
            }

    let ledger: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let scope = TenantAuth.requireScope ctx
                let! rows = PenaltyLedgerRepository.listForTenant dataSource scope 100

                let ledgerRows =
                    rows
                    |> List.map (fun row ->
                        $"""<tr><td>{Html.enc row.ContractReference}</td><td>{Html.enc row.ClauseReference}</td><td>{Html.enc row.CounterpartyName}</td><td>{Html.enc (DomainMapping.ledgerEntryKindText row.Entry.EntryKind)}</td><td>{Html.enc (DomainMapping.ledgerDirectionText row.Entry.Direction)}</td><td>{Html.money (Money.cents row.Entry.Amount) (Money.currency row.Entry.Amount)}</td></tr>""")
                    |> String.concat ""

                let body =
                    Html.pageHeader "Ledger" "Append-only bilateral penalty entries."
                    + $"""<section class="panel"><table><thead><tr><th>Contract</th><th>Clause</th><th>Counterparty</th><th>Kind</th><th>Direction</th><th>Amount</th></tr></thead><tbody>{ledgerRows}</tbody></table></section>"""

                return! UiHttp.html "Ledger" "Ledger" body next ctx
            }
