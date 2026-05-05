namespace Slapen.Ui.Tests

open System
open System.IO
open Microsoft.Playwright
open Xunit

type CsvSettlementFlowTests(fixture: UiServerFixture) =
    let pageAsync () =
        task {
            let! playwright = Playwright.CreateAsync()
            let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
            let! page = browser.NewPageAsync()
            return playwright, browser, page
        }

    let login (page: IPage) =
        task {
            let! _ = page.GotoAsync($"{fixture.BaseUrl}/login")
            do! page.GetByLabel("API key").FillAsync(TestKeys.tenantA)
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Sign in")).ClickAsync()
            do! page.WaitForURLAsync($"{fixture.BaseUrl}/")
        }

    [<Fact>]
    member _.``operator uploads CSV accrues posts and downloads settlement PDF``() =
        task {
            let! playwright, browser, page = pageAsync ()
            let sourceRef = $"ui-csv-{Guid.NewGuid():N}"
            let csvPath = Path.Combine(Path.GetTempPath(), $"{sourceRef}.csv")
            let pdfPath = Path.Combine(Path.GetTempPath(), $"{sourceRef}.pdf")

            let csv =
                String.concat
                    Environment.NewLine
                    [ "source_ref,contract_id,sla_clause_id,metric_value,units_missed,observed_at,reported_at"
                      $"{sourceRef},12000000-0000-0000-0000-000000000001,13000000-0000-0000-0000-000000000001,89.1,,2026-05-05T11:00:00Z,2026-05-05T11:01:00Z" ]

            File.WriteAllText(csvPath, csv)

            try
                do! login page
                do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Breaches")).ClickAsync()
                do! page.GetByLabel("CSV breach file").SetInputFilesAsync(csvPath)
                do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Upload CSV")).ClickAsync()
                do! page.Locator("table tbody tr").First.Locator("a").ClickAsync()
                do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Accrue")).ClickAsync()
                let! accrualRows = page.GetByText("accrual").CountAsync()
                Assert.True(accrualRows >= 2)

                do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Settlements")).ClickAsync()
                do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Build settlements")).ClickAsync()
                do! page.Locator("table tbody tr").First.Locator("a").ClickAsync()
                do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Approve")).ClickAsync()
                do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Post local PDF")).ClickAsync()
                do! page.GetByText("posted").WaitForAsync()

                let! download =
                    page.RunAndWaitForDownloadAsync(fun () ->
                        page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Download PDF")).ClickAsync())

                do! download.SaveAsAsync(pdfPath)
                let pdf = FileInfo(pdfPath)
                Assert.True(pdf.Length > 100L)
                do! browser.CloseAsync()
                playwright.Dispose()
            finally
                if File.Exists csvPath then
                    File.Delete csvPath

                if File.Exists pdfPath then
                    File.Delete pdfPath
        }

    interface IClassFixture<UiServerFixture>

module CsvSettlementFlowTestsAssemblyMarker =
    let value = 0
