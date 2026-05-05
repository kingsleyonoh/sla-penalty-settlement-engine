namespace Slapen.Templates

open System.Globalization
open QuestPDF.Fluent
open QuestPDF.Helpers
open QuestPDF.Infrastructure

[<RequireQualifiedAccess>]
module PdfRenderer =
    let private moneyText cents currency =
        let amount = decimal cents / 100M
        sprintf "%s %s" currency (amount.ToString("N2", CultureInfo.InvariantCulture))

    let private textOrEmpty value =
        if System.String.IsNullOrWhiteSpace value then "" else value

    let renderSettlement (snapshot: SettlementSnapshot) =
        QuestPDF.Settings.License <- LicenseType.Community

        Document
            .Create(fun container ->
                container.Page(fun page ->
                    page.Size(PageSizes.A4) |> ignore
                    page.Margin(40.0f) |> ignore
                    page.DefaultTextStyle(fun style -> style.FontSize(10.0f)) |> ignore

                    page
                        .Header()
                        .Column(fun column ->
                            column.Item().Text("SLA Settlement Credit Note").FontSize(18.0f).Bold()
                            |> ignore

                            column.Item().Text(snapshot.SettlementNumber).FontSize(10.0f) |> ignore)

                    page
                        .Content()
                        .Column(fun column ->
                            column.Spacing(10.0f) |> ignore
                            column.Item().Text(snapshot.Tenant.DisplayName).FontSize(14.0f).Bold() |> ignore
                            column.Item().Text(snapshot.Tenant.FullLegalName) |> ignore

                            column.Item().Text(textOrEmpty snapshot.Tenant.AddressJson).FontSize(8.0f)
                            |> ignore

                            column.Item().LineHorizontal(1.0f) |> ignore

                            column.Item().Text(sprintf "Counterparty: %s" snapshot.Counterparty.CanonicalName)
                            |> ignore

                            column
                                .Item()
                                .Text(
                                    sprintf "Contract: %s - %s" snapshot.Contract.Reference snapshot.Contract.Title
                                )
                            |> ignore

                            column.Item().Text(sprintf "Period: %O to %O" snapshot.PeriodStart snapshot.PeriodEnd)
                            |> ignore

                            column
                                .Item()
                                .Text(sprintf "Total credit: %s" (moneyText snapshot.AmountCents snapshot.Currency))
                                .FontSize(13.0f)
                                .Bold()
                            |> ignore

                            column
                                .Item()
                                .Table(fun table ->
                                    table.ColumnsDefinition(fun columns ->
                                        columns.RelativeColumn(4.0f) |> ignore
                                        columns.RelativeColumn(2.0f) |> ignore
                                        columns.RelativeColumn(2.0f) |> ignore)

                                    table.Header(fun header ->
                                        header.Cell().Text("Clause").Bold() |> ignore
                                        header.Cell().Text("Breach").Bold() |> ignore
                                        header.Cell().Text("Amount").Bold() |> ignore)

                                    for line in snapshot.Lines do
                                        table.Cell().Text(line.ClauseReference) |> ignore

                                        table.Cell().Text(line.BreachEventId.ToString("N").Substring(0, 12))
                                        |> ignore

                                        table.Cell().Text(moneyText line.AmountCents line.Currency) |> ignore)
                            |> ignore)
                    |> ignore

                    page
                        .Footer()
                        .AlignCenter()
                        .Text(fun text ->
                            text.Span("Generated from immutable tenant snapshot captured at ") |> ignore
                            text.Span(snapshot.Tenant.CapturedAt.ToString("O")) |> ignore)
                    |> ignore)
                |> ignore)
            .GeneratePdf()
