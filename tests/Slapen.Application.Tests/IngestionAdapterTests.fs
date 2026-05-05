namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Application
open Slapen.Data
open Xunit

[<Collection("postgres")>]
type IngestionAdapterTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let acmeScope = TenantScope.create acmeTenantId
    let contractId = Guid.Parse "12000000-0000-0000-0000-000000000001"
    let clauseId = Guid.Parse "13000000-0000-0000-0000-000000000001"

    let scalar sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value
        }

    [<Fact>]
    member _.``manual ingestion records a run raw payload and tenant scoped breach``() : Task =
        task {
            let sourceRef = $"manual-ingestion-{Guid.NewGuid():N}"

            let! result =
                Ingestion.ingestManual
                    fixture.DataSource
                    acmeScope
                    { ContractId = contractId
                      SlaClauseId = clauseId
                      SourceRef = Some sourceRef
                      MetricValue = 91.25M
                      UnitsMissed = None
                      ObservedAt = DateTimeOffset.Parse "2026-05-05T08:00:00Z"
                      ReportedAt = DateTimeOffset.Parse "2026-05-05T08:05:00Z"
                      RawPayloadJson = """{"operator":"manual"}""" }

            result.Attempted |> should equal 1
            result.Stored |> should equal 1
            result.Rejected |> should equal 0

            let! runStatusValue =
                scalar
                    "select status from ingestion_runs where tenant_id = @tenant_id and id = @run_id"
                    [ "tenant_id", acmeTenantId :> obj; "run_id", result.RunId :> obj ]

            let runStatus = runStatusValue :?> string
            runStatus |> should equal "succeeded"

            let! rawPayloadValue =
                scalar
                    "select raw_payload::text from breach_events where tenant_id = @tenant_id and source = 'manual' and source_ref = @source_ref"
                    [ "tenant_id", acmeTenantId :> obj; "source_ref", sourceRef :> obj ]

            let rawPayload = rawPayloadValue :?> string
            rawPayload.Contains("operator") |> should equal true
        }

    [<Fact>]
    member _.``csv ingestion uses fixed headers and dedupes source refs per tenant``() : Task =
        task {
            let sourceRef = $"csv-ingestion-{Guid.NewGuid():N}"

            let csv =
                String.concat
                    "\n"
                    [ "source_ref,contract_id,sla_clause_id,metric_value,units_missed,observed_at,reported_at"
                      $"{sourceRef},{contractId},{clauseId},88.5,,2026-05-06T09:00:00Z,2026-05-06T09:01:00Z"
                      $"{sourceRef},{contractId},{clauseId},88.5,,2026-05-06T09:00:00Z,2026-05-06T09:01:00Z" ]

            let! result = Ingestion.ingestCsv fixture.DataSource acmeScope csv

            result.Attempted |> should equal 2
            result.Stored |> should equal 1
            result.Rejected |> should equal 1
            result.Status |> should equal "partial"

            let! breachCountValue =
                scalar
                    "select count(*)::bigint from breach_events where tenant_id = @tenant_id and source = 'csv_import' and source_ref = @source_ref"
                    [ "tenant_id", acmeTenantId :> obj; "source_ref", sourceRef :> obj ]

            let breachCount = breachCountValue :?> int64
            breachCount |> should equal 1L
        }

    [<Fact>]
    member _.``csv ingestion rejects custom mapper headers``() : Task =
        task {
            let csv =
                String.concat "\n" [ "contract,clause,value"; $"{contractId},{clauseId},88.5" ]

            let! result = Ingestion.ingestCsv fixture.DataSource acmeScope csv

            result.Attempted |> should equal 0
            result.Stored |> should equal 0
            result.Rejected |> should equal 1
            result.Status |> should equal "failed"
        }
