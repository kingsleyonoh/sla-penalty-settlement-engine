namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Data
open Slapen.Ecosystem
open Slapen.Jobs
open Xunit

[<Collection("postgres")>]
type ContractLifecycleJobTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let acmeScope = TenantScope.create acmeTenantId

    let scalarInt64 sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value :?> int64
        }

    let breach sourceRef =
        { SourceRef = sourceRef
          ContractRef = "contract-acme-001"
          ClauseRef = "Schedule B 2.3.1"
          MetricValue = 87.25M
          UnitsMissed = Some 2.0M
          ObservedAt = DateTimeOffset.Parse "2026-05-05T09:00:00Z"
          ReportedAt = DateTimeOffset.Parse "2026-05-05T09:01:00Z"
          RawPayloadJson = """{"event_id":"contract-lifecycle-job"}""" }

    [<Fact>]
    member _.``REST backfill ingests Contract Lifecycle breaches idempotently``() : Task =
        task {
            let sourceRef = $"cl-rest-{Guid.NewGuid():N}"

            let! first =
                ContractLifecycleRestBackfillJob.execute
                    fixture.DataSource
                    acmeScope
                    (fun _ -> Task.FromResult [ breach sourceRef ])
                    (DateTimeOffset.Parse "2026-05-05T00:00:00Z")

            let! second =
                ContractLifecycleRestBackfillJob.execute
                    fixture.DataSource
                    acmeScope
                    (fun _ -> Task.FromResult [ breach sourceRef ])
                    (DateTimeOffset.Parse "2026-05-05T00:00:00Z")

            first.Stored |> should equal 1
            second.Stored |> should equal 0

            let! count =
                scalarInt64
                    "select count(*)::bigint from breach_events where tenant_id = @tenant_id and source = 'contract_lifecycle_rest' and source_ref = @source_ref"
                    [ "tenant_id", acmeTenantId :> obj; "source_ref", sourceRef :> obj ]

            count |> should equal 1L
        }

    [<Fact>]
    member _.``NATS consumer handler persists staged Contract Lifecycle events``() : Task =
        task {
            let sourceRef = $"cl-nats-{Guid.NewGuid():N}"

            let payload =
                $"""{{"event_id":"{sourceRef}","contract_ref":"contract-acme-001","clause_ref":"Schedule B 2.3.1","metric_value":86.75,"units_missed":1.5,"observed_at":"2026-05-06T09:00:00Z","reported_at":"2026-05-06T09:02:00Z"}}"""

            let! result = ContractLifecycleNatsConsumer.handleMessage fixture.DataSource acmeScope payload

            result.Stored |> should equal 1

            let! count =
                scalarInt64
                    "select count(*)::bigint from breach_events where tenant_id = @tenant_id and source = 'contract_lifecycle_nats' and source_ref = @source_ref"
                    [ "tenant_id", acmeTenantId :> obj; "source_ref", sourceRef :> obj ]

            count |> should equal 1L
        }
