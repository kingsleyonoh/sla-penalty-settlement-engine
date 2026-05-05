namespace Slapen.Api.Tests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.Testing
open Microsoft.Extensions.Configuration
open Npgsql
open Testcontainers.PostgreSql
open Xunit

module TestKeys =
    let tenantA = "slapen_a_t006_placeholder"
    let tenantB = "slapen_b_t006_placeholder"

module Database =
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
            use runnerProcess = new Process()
            runnerProcess.StartInfo.FileName <- "dotnet"
            runnerProcess.StartInfo.WorkingDirectory <- repoRoot ()
            runnerProcess.StartInfo.ArgumentList.Add("run")
            runnerProcess.StartInfo.ArgumentList.Add("--project")
            runnerProcess.StartInfo.ArgumentList.Add("db/Migrate")
            runnerProcess.StartInfo.ArgumentList.Add("--")
            runnerProcess.StartInfo.ArgumentList.Add("--connection")
            runnerProcess.StartInfo.ArgumentList.Add(connectionString)
            runnerProcess.StartInfo.ArgumentList.Add("--seed-fixtures")
            runnerProcess.StartInfo.RedirectStandardOutput <- true
            runnerProcess.StartInfo.RedirectStandardError <- true
            runnerProcess.StartInfo.UseShellExecute <- false

            if not (runnerProcess.Start()) then
                failwith "Migration runner did not start."

            let! stdout = runnerProcess.StandardOutput.ReadToEndAsync()
            let! stderr = runnerProcess.StandardError.ReadToEndAsync()
            do! runnerProcess.WaitForExitAsync()

            if runnerProcess.ExitCode <> 0 then
                failwith
                    $"Migration runner failed with exit code {runnerProcess.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}"
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

type PostgresFixture() =
    let postgres =
        PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("slapen_api_test")
            .WithUsername("slapen")
            .WithPassword("slapen")
            .Build()

    member _.ConnectionString = postgres.GetConnectionString()

    interface IAsyncLifetime with
        member this.InitializeAsync() : ValueTask =
            ValueTask(
                task {
                    do! postgres.StartAsync()
                    do! Database.runMigrations this.ConnectionString
                    do! Database.updateFixtureKeys this.ConnectionString
                    Environment.SetEnvironmentVariable("DATABASE_URL", this.ConnectionString)
                    Environment.SetEnvironmentVariable("SLAPEN_RATE_LIMIT_PER_MINUTE", "1000")
                }
            )

        member _.DisposeAsync() : ValueTask =
            ValueTask(postgres.DisposeAsync().AsTask())

type ApiFactory(fixture: PostgresFixture) =
    inherit WebApplicationFactory<Slapen.Api.AppMarker>()

    override _.ConfigureWebHost(builder: IWebHostBuilder) =
        builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration(fun _ config ->
                let values =
                    dict
                        [ "DATABASE_URL", fixture.ConnectionString
                          "SLAPEN_RATE_LIMIT_PER_MINUTE", "1000"
                          "HUB_INGRESS_SECRET", "fake_hub_ingress_secret" ]

                config.AddInMemoryCollection(values) |> ignore)
        |> ignore
