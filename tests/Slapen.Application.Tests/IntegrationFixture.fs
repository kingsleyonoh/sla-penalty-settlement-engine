namespace Slapen.Application.Tests

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open Npgsql
open Testcontainers.PostgreSql
open Xunit

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

type PostgresFixture() =
    let postgres =
        PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("slapen_application_test")
            .WithUsername("slapen")
            .WithPassword("slapen")
            .Build()

    let mutable dataSource: NpgsqlDataSource option = None

    member _.DataSource =
        match dataSource with
        | Some source -> source
        | None -> failwith "PostgreSQL fixture has not been initialized."

    interface IAsyncLifetime with
        member _.InitializeAsync() : ValueTask =
            ValueTask(
                task {
                    do! postgres.StartAsync()
                    do! Database.runMigrations (postgres.GetConnectionString())
                    dataSource <- Some(NpgsqlDataSource.Create(postgres.GetConnectionString()))
                }
            )

        member _.DisposeAsync() : ValueTask =
            ValueTask(
                task {
                    match dataSource with
                    | Some source -> do! source.DisposeAsync().AsTask()
                    | None -> ()

                    do! postgres.DisposeAsync().AsTask()
                }
            )
