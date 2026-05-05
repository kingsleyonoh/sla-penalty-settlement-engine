module Slapen.Data.Tests.MigrationTests

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Testcontainers.PostgreSql
open Xunit

let private repoRoot () =
    let rec walk (directory: DirectoryInfo) =
        if File.Exists(Path.Combine(directory.FullName, "Slapen.sln")) then
            directory.FullName
        elif isNull directory.Parent then
            failwith "Could not find repository root."
        else
            walk directory.Parent

    walk (DirectoryInfo(Directory.GetCurrentDirectory()))

let private runMigrations connectionString : Task<string> =
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

        let started = runnerProcess.Start()
        started |> should equal true

        let! stdout = runnerProcess.StandardOutput.ReadToEndAsync()
        let! stderr = runnerProcess.StandardError.ReadToEndAsync()
        do! runnerProcess.WaitForExitAsync()

        if runnerProcess.ExitCode <> 0 then
            failwith
                $"Migration runner failed with exit code {runnerProcess.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}"

        return stdout
    }

let private scalar<'T> (connection: NpgsqlConnection) sql : Task<'T> =
    task {
        use command = new NpgsqlCommand(sql, connection)
        let! value = command.ExecuteScalarAsync()
        return value :?> 'T
    }

let private execute (connection: NpgsqlConnection) sql : Task =
    task {
        use command = new NpgsqlCommand(sql, connection)
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private createPostgres () =
    PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("slapen_migration_test")
        .WithUsername("slapen")
        .WithPassword("slapen")
        .Build()

[<Fact>]
let ``migration runner applies schema seeds fixtures and ledger immutability`` () : Task =
    task {
        let postgres = createPostgres ()

        try
            do! postgres.StartAsync()

            let connectionString = postgres.GetConnectionString()
            let! output = runMigrations connectionString

            output.Contains("Applied") |> should equal true

            use connection = new NpgsqlConnection(connectionString)
            do! connection.OpenAsync()

            let expectedTables =
                [ "tenants"
                  "users"
                  "counterparties"
                  "contracts"
                  "sla_clauses"
                  "breach_events"
                  "penalty_ledger"
                  "settlements"
                  "settlement_ledger_entries"
                  "outbox"
                  "ingestion_runs"
                  "ingestion_adapter_settings"
                  "audit_log"
                  "signal_definitions" ]

            for tableName in expectedTables do
                let! exists = scalar<bool> connection $"select to_regclass('public.{tableName}') is not null"

                exists |> should equal true

            let! migratedCount = scalar<int64> connection "select count(*) from schema_migrations"

            migratedCount |> should equal 18L

            let tenantScopedTables =
                expectedTables |> List.except [ "tenants"; "signal_definitions" ]

            for tableName in tenantScopedTables do
                let! tenantColumnCount =
                    scalar<int64>
                        connection
                        $"select count(*) from information_schema.columns where table_schema = 'public' and table_name = '{tableName}' and column_name = 'tenant_id'"

                tenantColumnCount |> should equal 1L

            let! signalCount = scalar<int64> connection "select count(*) from signal_definitions"

            signalCount |> should be (greaterThanOrEqualTo 3L)

            let! tenantCount = scalar<int64> connection "select count(*) from tenants"

            tenantCount |> should equal 2L

            let! distinctIdentityCount =
                scalar<int64>
                    connection
                    "select count(distinct legal_name || '|' || full_legal_name || '|' || display_name || '|' || locale || '|' || timezone || '|' || default_currency) from tenants"

            distinctIdentityCount |> should equal 2L

            let insertSql =
                """
                insert into penalty_ledger (
                    id,
                    tenant_id,
                    sla_clause_id,
                    breach_event_id,
                    counterparty_id,
                    contract_id,
                    entry_kind,
                    direction,
                    amount_cents,
                    currency,
                    accrual_period_start,
                    accrual_period_end,
                    reason_code,
                    created_by_kind
                )
                select
                    '90000000-0000-0000-0000-000000000001'::uuid,
                    t.id,
                    sc.id,
                    b.id,
                    cp.id,
                    c.id,
                    'accrual',
                    'credit_owed_to_us',
                    10000,
                    c.currency,
                    '2026-05-01T00:00:00Z',
                    '2026-05-02T00:00:00Z',
                    'sla_breach',
                    'system'
                from tenants t
                join counterparties cp on cp.tenant_id = t.id
                join contracts c on c.tenant_id = t.id and c.counterparty_id = cp.id
                join sla_clauses sc on sc.tenant_id = t.id and sc.contract_id = c.id
                join breach_events b on b.tenant_id = t.id and b.contract_id = c.id and b.sla_clause_id = sc.id
                where t.slug = 'acme-gmbh-de'
                limit 1
                """

            do! execute connection insertSql

            let! updateBlocked =
                task {
                    try
                        do!
                            execute
                                connection
                                "update penalty_ledger set amount_cents = 20000 where id = '90000000-0000-0000-0000-000000000001'::uuid"

                        return false
                    with :? PostgresException as error ->
                        return error.SqlState = "P0001"
                }

            updateBlocked |> should equal true

            let! deleteBlocked =
                task {
                    try
                        do!
                            execute
                                connection
                                "delete from penalty_ledger where id = '90000000-0000-0000-0000-000000000001'::uuid"

                        return false
                    with :? PostgresException as error ->
                        return error.SqlState = "P0001"
                }

            deleteBlocked |> should equal true

            do! postgres.DisposeAsync().AsTask()
        with error ->
            do! postgres.DisposeAsync().AsTask()
            return raise error
    }
