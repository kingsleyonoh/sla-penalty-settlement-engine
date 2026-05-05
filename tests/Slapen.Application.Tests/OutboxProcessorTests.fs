namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Application
open Slapen.Data
open Xunit

[<Collection("postgres")>]
type OutboxProcessorTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let acmeScope = TenantScope.create acmeTenantId

    let execute sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! _ = command.ExecuteNonQueryAsync()
            return ()
        }

    let scalarInt sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return Convert.ToInt32 value
        }

    let scalarString sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value :?> string
        }

    [<Fact>]
    member _.``outbox processor retries due failures and eventually marks done``() : Task =
        task {
            let outboxId = Guid.Parse "65000000-0000-0000-0000-000000000001"
            let mutable calls = 0

            let! _ =
                OutboxRepository.enqueue
                    fixture.DataSource
                    acmeScope
                    { Id = outboxId
                      Operation = "hub.emit"
                      PayloadJson = """{"event_type":"settlement.posted","event_id":"evt-1","payload":{}}"""
                      IdempotencyKey = Some "evt-1"
                      NextRunAt = DateTimeOffset.Parse "2026-05-05T09:00:00Z" }

            let handler _ =
                task {
                    calls <- calls + 1

                    if calls = 1 then
                        return Error "temporary outage"
                    else
                        return Ok()
                }

            let options =
                { BatchSize = 5
                  MaxAttempts = 3
                  LeaseSeconds = 30
                  RetryDelaySeconds = 1 }

            let! first =
                OutboxProcessor.processDue
                    fixture.DataSource
                    options
                    handler
                    (DateTimeOffset.Parse "2026-05-05T10:00:00Z")

            do!
                execute
                    "update outbox set next_run_at = @now where id = @id"
                    [ "id", outboxId :> obj
                      "now", DateTimeOffset.Parse "2026-05-05T10:00:01Z" :> obj ]

            let! second =
                OutboxProcessor.processDue
                    fixture.DataSource
                    options
                    handler
                    (DateTimeOffset.Parse "2026-05-05T10:00:01Z")

            let! status = scalarString "select status from outbox where id = @id" [ "id", outboxId :> obj ]
            let! attempts = scalarInt "select attempts from outbox where id = @id" [ "id", outboxId :> obj ]

            first.Failed |> should equal 1
            second.Done |> should equal 1
            calls |> should equal 2
            attempts |> should equal 2
            status |> should equal "done"
        }

    [<Fact>]
    member _.``outbox processor dead letters after max attempts``() : Task =
        task {
            let outboxId = Guid.Parse "65000000-0000-0000-0000-000000000002"

            let! _ =
                OutboxRepository.enqueue
                    fixture.DataSource
                    acmeScope
                    { Id = outboxId
                      Operation = "hub.emit"
                      PayloadJson = """{"event_type":"settlement.failed","event_id":"evt-2","payload":{}}"""
                      IdempotencyKey = Some "evt-2"
                      NextRunAt = DateTimeOffset.Parse "2026-05-05T09:00:00Z" }

            let handler _ =
                task { return Error "permanent failure" }

            let options =
                { BatchSize = 5
                  MaxAttempts = 1
                  LeaseSeconds = 30
                  RetryDelaySeconds = 1 }

            let! result =
                OutboxProcessor.processDue
                    fixture.DataSource
                    options
                    handler
                    (DateTimeOffset.Parse "2026-05-05T10:00:00Z")

            let! status = scalarString "select status from outbox where id = @id" [ "id", outboxId :> obj ]

            result.Dead |> should equal 1
            status |> should equal "dead"
        }
