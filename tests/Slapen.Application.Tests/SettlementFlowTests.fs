namespace Slapen.Application.Tests

open System
open System.Text.Json
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Application
open Slapen.Data
open Slapen.Domain
open Slapen.Templates
open Xunit

[<Collection("postgres")>]
type SettlementFlowTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let globexTenantId = Guid.Parse "20000000-0000-0000-0000-000000000001"
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

    let scalarString sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value :?> string
        }

    let seedAccrual breachId sourceRef (observedAt: DateTimeOffset) =
        task {
            do!
                execute
                    """
                    insert into breach_events (
                        id, tenant_id, contract_id, sla_clause_id, source, source_ref,
                        metric_value, observed_at, reported_at, raw_payload, status
                    )
                    values (
                        @breach_id, @tenant_id, '12000000-0000-0000-0000-000000000001',
                        '13000000-0000-0000-0000-000000000001', 'manual', @source_ref,
                        93.0000, @observed_at, @observed_at, '{}'::jsonb, 'pending'
                    )
                    on conflict (tenant_id, source, source_ref) where source_ref is not null do nothing
                    """
                    [ "breach_id", breachId :> obj
                      "tenant_id", acmeTenantId :> obj
                      "source_ref", sourceRef :> obj
                      "observed_at", observedAt :> obj ]

            let! result = AccrualWorker.processBreach fixture.DataSource acmeScope breachId (observedAt.AddHours 2.0)

            match result with
            | AccrualWorker.Accrued _ -> ()
            | other -> failwithf "Expected accrual, got %A" other
        }

    let resetAcmeIdentity () =
        execute
            """
            update tenants
            set legal_name = 'Acme GmbH',
                full_legal_name = 'Acme Beschaffung GmbH',
                display_name = 'Acme Procurement DE'
            where id = @tenant_id
            """
            [ "tenant_id", acmeTenantId :> obj ]

    [<Fact>]
    member _.``settlement builder freezes tenant snapshot and renders PDF bytes``() : Task =
        task {
            let firstBreach = Guid.Parse "64000000-0000-0000-0000-000000000001"
            let secondBreach = Guid.Parse "64000000-0000-0000-0000-000000000002"
            let asOf = DateTimeOffset.Parse "2026-05-31T23:00:00Z"

            do! resetAcmeIdentity ()
            do! seedAccrual firstBreach "settlement-builder-001" (DateTimeOffset.Parse "2026-05-03T09:00:00Z")
            do! seedAccrual secondBreach "settlement-builder-002" (DateTimeOffset.Parse "2026-05-04T09:00:00Z")

            let! result =
                SettlementBuilder.buildPending
                    fixture.DataSource
                    acmeScope
                    (DateOnly(2026, 5, 1))
                    (DateOnly(2026, 5, 31))
                    CreatedBy.System
                    asOf

            match result with
            | SettlementBuilder.Built [ built ] ->
                built.AmountCents |> should equal 100000L
                built.PdfBytes.Length |> should be (greaterThan 100)

                built.PdfBytes[0..3]
                |> should equal [| byte '%'; byte 'P'; byte 'D'; byte 'F' |]

                let json = SettlementPdf.serializeSnapshot built.Snapshot
                json.Contains("Acme GmbH") |> should equal true
                json.Contains("Globex Inc.") |> should equal false
                json.Contains(globexTenantId.ToString()) |> should equal false

                use document = JsonDocument.Parse json
                let tenant = document.RootElement.GetProperty("tenant")
                tenant.GetProperty("legal_name").GetString() |> should equal "Acme GmbH"

                tenant.GetProperty("display_name").GetString()
                |> should equal "Acme Procurement DE"
            | other -> failwithf "Expected one built settlement, got %A" other
        }

    [<Fact>]
    member _.``local PDF posting reuses frozen snapshot after tenant identity changes``() : Task =
        task {
            let breachId = Guid.Parse "64000000-0000-0000-0000-000000000003"
            let asOf = DateTimeOffset.Parse "2026-05-31T23:00:00Z"

            do! resetAcmeIdentity ()
            do! seedAccrual breachId "settlement-poster-001" (DateTimeOffset.Parse "2026-05-05T09:00:00Z")

            let! built =
                SettlementBuilder.buildPending
                    fixture.DataSource
                    acmeScope
                    (DateOnly(2026, 5, 1))
                    (DateOnly(2026, 5, 31))
                    CreatedBy.System
                    asOf

            let settlementId =
                match built with
                | SettlementBuilder.Built [ item ] -> item.SettlementId
                | other -> failwithf "Expected one settlement, got %A" other

            do!
                execute
                    "update tenants set legal_name = 'Changed Legal Name' where id = @tenant_id"
                    [ "tenant_id", acmeTenantId :> obj ]

            let! posted =
                SettlementPoster.postReadySettlement
                    fixture.DataSource
                    acmeScope
                    { InvoiceReconEnabled = false
                      LocalPdfDirectory = None }
                    settlementId
                    (DateTimeOffset.Parse "2026-06-01T09:00:00Z")

            let! snapshot =
                scalarString
                    "select pdf_snapshot_json::text from settlements where id = @settlement_id"
                    [ "settlement_id", settlementId :> obj ]

            match posted with
            | SettlementPoster.PostedLocalPdf path -> path |> should startWith "local-pdf://"
            | other -> failwithf "Expected local PDF posting, got %A" other

            snapshot.Contains("Acme GmbH") |> should equal true
            snapshot.Contains("Changed Legal Name") |> should equal false
        }
