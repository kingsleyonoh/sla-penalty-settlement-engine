module Slapen.Data.Tests.PostgreSqlSmokeTests

open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Testcontainers.PostgreSql
open Xunit

[<Fact>]
let ``PostgreSQL 16 container accepts a deterministic query`` () : Task =
    task {
        let postgres =
            PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("slapen_test")
                .WithUsername("slapen")
                .WithPassword("slapen")
                .Build()

        try
            do! postgres.StartAsync()

            use connection = new NpgsqlConnection(postgres.GetConnectionString())
            do! connection.OpenAsync()

            use command =
                new NpgsqlCommand("select current_setting('server_version_num')::int >= 160000", connection)

            let! result = command.ExecuteScalarAsync()

            result |> should equal true
            do! postgres.DisposeAsync().AsTask()
        with error ->
            do! postgres.DisposeAsync().AsTask()
            return raise error
    }
