namespace Slapen.Ui.Tests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Microsoft.Playwright
open Npgsql
open Testcontainers.PostgreSql
open Xunit

module TestKeys =
    let tenantA = "slapen_a_t007_placeholder"
    let tenantB = "slapen_b_t007_placeholder"

module TestDatabase =
    let repoRoot () =
        let rec walk (directory: DirectoryInfo) =
            if File.Exists(Path.Combine(directory.FullName, "Slapen.sln")) then
                directory.FullName
            elif isNull directory.Parent then
                failwith "Could not find repository root."
            else
                walk directory.Parent

        walk (DirectoryInfo(Directory.GetCurrentDirectory()))

    let private sha256 (value: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes value)
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let private prefix (value: string) = value.Substring(0, 12)

    let runMigrations connectionString : Task =
        task {
            use runner = new Process()
            runner.StartInfo.FileName <- "dotnet"
            runner.StartInfo.WorkingDirectory <- repoRoot ()
            runner.StartInfo.ArgumentList.Add("run")
            runner.StartInfo.ArgumentList.Add("--project")
            runner.StartInfo.ArgumentList.Add("db/Migrate")
            runner.StartInfo.ArgumentList.Add("--")
            runner.StartInfo.ArgumentList.Add("--connection")
            runner.StartInfo.ArgumentList.Add(connectionString)
            runner.StartInfo.ArgumentList.Add("--seed-fixtures")
            runner.StartInfo.RedirectStandardOutput <- true
            runner.StartInfo.RedirectStandardError <- true
            runner.StartInfo.UseShellExecute <- false

            if not (runner.Start()) then
                failwith "Migration runner did not start."

            let! stdout = runner.StandardOutput.ReadToEndAsync()
            let! stderr = runner.StandardError.ReadToEndAsync()
            do! runner.WaitForExitAsync()

            if runner.ExitCode <> 0 then
                failwith $"Migration failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}"
        }

    let updateFixtureKeys connectionString : Task =
        task {
            use connection = new NpgsqlConnection(connectionString)
            do! connection.OpenAsync()

            use command =
                new NpgsqlCommand(
                    """
                    update tenants
                    set api_key_prefix = @tenant_a_prefix, api_key_hash = @tenant_a_hash
                    where id = '10000000-0000-0000-0000-000000000001';

                    update tenants
                    set api_key_prefix = @tenant_b_prefix, api_key_hash = @tenant_b_hash
                    where id = '20000000-0000-0000-0000-000000000001';
                    """,
                    connection
                )

            command.Parameters.AddWithValue("tenant_a_prefix", prefix TestKeys.tenantA)
            |> ignore

            command.Parameters.AddWithValue("tenant_a_hash", sha256 TestKeys.tenantA)
            |> ignore

            command.Parameters.AddWithValue("tenant_b_prefix", prefix TestKeys.tenantB)
            |> ignore

            command.Parameters.AddWithValue("tenant_b_hash", sha256 TestKeys.tenantB)
            |> ignore

            let! _ = command.ExecuteNonQueryAsync()
            ()
        }

type UiServerFixture() =
    let postgres =
        PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("slapen_ui_test")
            .WithUsername("slapen")
            .WithPassword("slapen")
            .Build()

    let port =
        use listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        let value = (listener.LocalEndpoint :?> IPEndPoint).Port
        listener.Stop()
        value

    let baseUrl = $"http://127.0.0.1:{port}"
    let mutable server: Process option = None

    member _.BaseUrl = baseUrl

    member private _.StartServer connectionString =
        let startInfo = ProcessStartInfo()
        startInfo.FileName <- "dotnet"
        startInfo.WorkingDirectory <- TestDatabase.repoRoot ()
        startInfo.ArgumentList.Add("run")
        startInfo.ArgumentList.Add("--project")
        startInfo.ArgumentList.Add("src/Slapen.Api")
        startInfo.ArgumentList.Add("--no-launch-profile")
        startInfo.ArgumentList.Add("--urls")
        startInfo.ArgumentList.Add(baseUrl)
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.UseShellExecute <- false
        startInfo.Environment["DATABASE_URL"] <- connectionString
        startInfo.Environment["SLAPEN_RATE_LIMIT_PER_MINUTE"] <- "1000"
        let serverProcess = Process.Start(startInfo)
        server <- Some serverProcess

    member private _.WaitForReady() =
        task {
            use client = new HttpClient()
            let deadline = DateTimeOffset.UtcNow.AddSeconds(60.0)
            let mutable ready = false

            while not ready && DateTimeOffset.UtcNow < deadline do
                try
                    use! response = client.GetAsync($"{baseUrl}/api/health")
                    ready <- response.StatusCode = HttpStatusCode.OK
                with _ ->
                    do! Task.Delay(500)

            if not ready then
                failwith "UI server did not become ready."
        }

    interface IAsyncLifetime with
        member this.InitializeAsync() : Task =
            task {
                do! postgres.StartAsync()
                do! TestDatabase.runMigrations (postgres.GetConnectionString())
                do! TestDatabase.updateFixtureKeys (postgres.GetConnectionString())
                this.StartServer(postgres.GetConnectionString())
                do! this.WaitForReady()
            }

        member _.DisposeAsync() : Task =
            task {
                match server with
                | Some serverProcess when not serverProcess.HasExited ->
                    serverProcess.Kill(entireProcessTree = true)
                    do! serverProcess.WaitForExitAsync()
                    serverProcess.Dispose()
                | Some serverProcess -> serverProcess.Dispose()
                | None -> ()

                do! postgres.DisposeAsync().AsTask()
            }

type UiFlowTests(fixture: UiServerFixture) =
    let pageAsync () =
        task {
            let! playwright = Playwright.CreateAsync()
            let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
            let! page = browser.NewPageAsync()
            return playwright, browser, page
        }

    [<Fact>]
    member _.``protected dashboard redirects to login and authenticates with API key session``() =
        task {
            let! playwright, browser, page = pageAsync ()

            let! _ = page.GotoAsync($"{fixture.BaseUrl}/")
            do! page.WaitForURLAsync(fixture.BaseUrl + "/login?returnUrl=%2F")

            do! page.GetByLabel("API key").FillAsync(TestKeys.tenantA)
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Sign in")).ClickAsync()
            do! page.WaitForURLAsync($"{fixture.BaseUrl}/")

            do! page.GetByTestId("tenant-display-name").WaitForAsync()
            let! globexVisible = page.GetByText("Globex", PageGetByTextOptions(Exact = false)).IsVisibleAsync()
            Assert.False(globexVisible)

            do! browser.CloseAsync()
            playwright.Dispose()
        }

    [<Fact>]
    member _.``operator can create contract clause breach reversal and see ledger timeline``() =
        task {
            let! playwright, browser, page = pageAsync ()
            let unique = Guid.NewGuid().ToString("N").Substring(0, 8)

            let! _ = page.GotoAsync($"{fixture.BaseUrl}/login")
            do! page.GetByLabel("API key").FillAsync(TestKeys.tenantA)
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Sign in")).ClickAsync()
            do! page.WaitForURLAsync($"{fixture.BaseUrl}/")

            do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Counterparties")).ClickAsync()
            do! page.GetByLabel("Counterparty name").FillAsync($"Supplier {unique}")
            do! page.GetByLabel("Country code").FillAsync("IE")
            do! page.GetByLabel("Default currency").FillAsync("EUR")
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Add counterparty")).ClickAsync()
            do! page.GetByText($"Supplier {unique}").WaitForAsync()

            do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Contracts")).ClickAsync()
            let! _ = page.GetByLabel("Counterparty").SelectOptionAsync(SelectOptionValue(Label = $"Supplier {unique}"))
            do! page.GetByLabel("Reference").FillAsync($"SLA-{unique}")
            do! page.GetByLabel("Title").FillAsync($"Availability Schedule {unique}")
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Add contract")).ClickAsync()
            do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = $"SLA-{unique}")).WaitForAsync()
            do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = $"SLA-{unique}")).ClickAsync()

            do! page.GetByLabel("Clause reference").FillAsync($"Schedule {unique}")
            do! page.GetByLabel("Metric").FillAsync("uptime_percent")
            do! page.GetByLabel("Target value").FillAsync("99.9")
            do! page.GetByLabel("Flat amount cents").FillAsync("75000")
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Add clause")).ClickAsync()
            do! page.GetByRole(AriaRole.Cell, PageGetByRoleOptions(Name = $"Schedule {unique}")).WaitForAsync()

            do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Breaches")).ClickAsync()

            let! _ =
                page
                    .GetByLabel("Clause")
                    .SelectOptionAsync(SelectOptionValue(Label = $"SLA-{unique} - Schedule {unique}"))

            do! page.GetByLabel("Metric value").FillAsync("91.1")
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Record breach")).ClickAsync()
            do! page.GetByText("pending").WaitForAsync()
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Accrue")).ClickAsync()
            let! accrualRows = page.GetByText("accrual").CountAsync()
            Assert.True(accrualRows >= 2)
            do! page.GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Reverse")).ClickAsync()
            let! reversalRows = page.GetByText("reversal").CountAsync()
            Assert.True(reversalRows >= 2)

            do! page.GetByRole(AriaRole.Link, PageGetByRoleOptions(Name = "Ledger")).ClickAsync()
            let! contractLedgerRows = page.GetByText($"SLA-{unique}").CountAsync()
            let! creditRows = page.GetByText("credit_owed_to_us").CountAsync()
            let! mirrorRows = page.GetByText("mirror").CountAsync()
            Assert.Equal(4, contractLedgerRows)
            Assert.True(creditRows >= 2)
            Assert.True(mirrorRows >= 2)

            do! browser.CloseAsync()
            playwright.Dispose()
        }

    interface IClassFixture<UiServerFixture>
