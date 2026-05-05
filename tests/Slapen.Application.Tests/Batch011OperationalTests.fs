namespace Slapen.Application.Tests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Application
open Slapen.Data
open Slapen.Domain
open Slapen.Jobs
open Xunit

type private FakeInvoiceHandler() =
    inherit HttpMessageHandler()

    let calls = ResizeArray<string>()

    member _.Calls = List.ofSeq calls

    override _.SendAsync(message, _cancellationToken) =
        task {
            calls.Add(message.RequestUri.AbsolutePath)

            return
                new HttpResponseMessage(
                    HttpStatusCode.Created,
                    Content = new StringContent("""{"id":"fake-credit-note"}""", Encoding.UTF8, "application/json")
                )
        }

[<Collection("postgres")>]
type Batch011OperationalTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let globexTenantId = Guid.Parse "20000000-0000-0000-0000-000000000001"
    let acmeScope = TenantScope.create acmeTenantId
    let globexScope = TenantScope.create globexTenantId

    let execute sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! _ = command.ExecuteNonQueryAsync()
            return ()
        }

    let scalarInt64 sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value :?> int64
        }

    let seedAccrual sourceRef =
        task {
            let breachId = Guid.NewGuid()

            do!
                execute
                    """
                    insert into breach_events (
                        id, tenant_id, contract_id, sla_clause_id, source, source_ref,
                        metric_value, observed_at, reported_at, raw_payload, status
                    )
                    values (
                        @breach_id, @tenant_id, '12000000-0000-0000-0000-000000000001',
                        '13000000-0000-0000-0000-000000000001', 'contract_lifecycle_nats',
                        @source_ref, 91.0000, @observed_at, @observed_at, '{}'::jsonb, 'pending'
                    )
                    """
                    [ "breach_id", breachId :> obj
                      "tenant_id", acmeTenantId :> obj
                      "source_ref", sourceRef :> obj
                      "observed_at", DateTimeOffset.Parse "2026-05-07T09:00:00Z" :> obj ]

            let! result =
                AccrualWorker.processBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    (DateTimeOffset.Parse "2026-05-07T10:00:00Z")

            match result with
            | AccrualWorker.Accrued _ -> return breachId
            | other -> return failwithf "Expected accrual, got %A" other
        }

    [<Fact>]
    member _.``ingestion adapter settings are tenant scoped and disabled adapters are no-op``() : Task =
        task {
            let adapter = "contract_lifecycle_rest"

            let! disabled = IngestionControl.testAdapter fixture.DataSource acmeScope adapter DateTimeOffset.UtcNow
            disabled |> should equal IngestionControl.AdapterDisabled

            let! enabled =
                IngestionSettingsRepository.setEnabled fixture.DataSource acmeScope adapter true DateTimeOffset.UtcNow

            enabled.Enabled |> should equal true

            let! tested = IngestionControl.testAdapter fixture.DataSource acmeScope adapter DateTimeOffset.UtcNow
            tested |> should equal IngestionControl.AdapterHealthy

            let! pull = IngestionControl.requestPullNow fixture.DataSource acmeScope adapter DateTimeOffset.UtcNow
            pull |> should equal IngestionControl.PullRequested

            let! acmeSettings = IngestionSettingsRepository.list fixture.DataSource acmeScope
            let! globexSettings = IngestionSettingsRepository.list fixture.DataSource globexScope

            (acmeSettings |> List.find (fun item -> item.Adapter = adapter)).Enabled
            |> should equal true

            (globexSettings |> List.find (fun item -> item.Adapter = adapter)).Enabled
            |> should equal false
        }

    [<Fact>]
    member _.``dashboard summary uses bounded aggregate queries under seeded load``() : Task =
        task {
            let breachCount =
                Environment.GetEnvironmentVariable("DASHBOARD_LOAD_BREACHES")
                |> Option.ofObj
                |> Option.bind (fun value ->
                    match Int32.TryParse value with
                    | true, parsed -> Some parsed
                    | false, _ -> None)
                |> Option.defaultValue 250

            let settlementCount =
                Environment.GetEnvironmentVariable("DASHBOARD_LOAD_SETTLEMENTS")
                |> Option.ofObj
                |> Option.bind (fun value ->
                    match Int32.TryParse value with
                    | true, parsed -> Some parsed
                    | false, _ -> None)
                |> Option.defaultValue 125

            do!
                execute
                    """
                    insert into breach_events (
                        id, tenant_id, contract_id, sla_clause_id, source, source_ref,
                        metric_value, observed_at, reported_at, raw_payload, status
                    )
                    select
                        md5('load-audit-' || gs::text)::uuid, @tenant_id, '12000000-0000-0000-0000-000000000001',
                        '13000000-0000-0000-0000-000000000001', 'manual',
                        'load-audit-' || gs::text, 92.0, @observed_at, @observed_at,
                        '{}'::jsonb, 'pending'
                    from generate_series(1, @breach_count) gs
                    on conflict (tenant_id, source, source_ref) where source_ref is not null do nothing
                    """
                    [ "tenant_id", acmeTenantId :> obj
                      "breach_count", breachCount :> obj
                      "observed_at", DateTimeOffset.Parse "2026-05-08T09:00:00Z" :> obj ]

            do!
                execute
                    """
                    insert into settlements (
                        id, tenant_id, counterparty_id, contract_id, currency, amount_cents,
                        status, period_start, period_end, created_at
                    )
                    select
                        md5('load-settlement-' || gs::text)::uuid, @tenant_id,
                        '11000000-0000-0000-0000-000000000001',
                        '12000000-0000-0000-0000-000000000001',
                        'EUR', 1000, 'posted', '2026-05-01', '2026-05-31', @observed_at
                    from generate_series(1, @settlement_count) gs
                    on conflict (id) do nothing
                    """
                    [ "tenant_id", acmeTenantId :> obj
                      "settlement_count", settlementCount :> obj
                      "observed_at", DateTimeOffset.Parse "2026-05-08T09:00:00Z" :> obj ]

            let started = DateTimeOffset.UtcNow
            let! summary = DashboardRepository.summary fixture.DataSource acmeScope 25
            let elapsed = DateTimeOffset.UtcNow - started

            summary.PendingBreaches |> should be (greaterThanOrEqualTo (int64 breachCount))
            summary.RecentPending.Length |> should be (lessThanOrEqualTo 25)
            summary.QueryCount |> should equal 1
            Assert.True(elapsed.TotalMilliseconds < 500.0, $"Dashboard summary took {elapsed.TotalMilliseconds}ms")
        }

    [<Fact>]
    member _.``fake staging ecosystem flow accrues settles posts and emits Hub event``() : Task =
        task {
            let sourceRef = $"staging-e2e-{Guid.NewGuid():N}"

            let payload =
                $"""{{"event_id":"{sourceRef}","contract_ref":"contract-acme-001","clause_ref":"Schedule B 2.3.1","metric_value":88.0,"units_missed":2.0,"observed_at":"2026-05-09T09:00:00Z","reported_at":"2026-05-09T09:01:00Z"}}"""

            let! ingested = ContractLifecycleNatsConsumer.handleMessage fixture.DataSource acmeScope payload
            ingested.Stored |> should equal 1

            let breachId = ingested.BreachIds.Head

            let! accrued =
                AccrualWorker.processBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    (DateTimeOffset.Parse "2026-05-09T10:00:00Z")

            match accrued with
            | AccrualWorker.Accrued ids -> ids.Length |> should equal 2
            | other -> failwithf "Expected accrual, got %A" other

            let! built =
                SettlementBuilder.buildPending
                    fixture.DataSource
                    acmeScope
                    (DateOnly(2026, 5, 1))
                    (DateOnly(2026, 5, 31))
                    CreatedBy.System
                    (DateTimeOffset.Parse "2026-05-31T23:00:00Z")

            let settlementId =
                match built with
                | SettlementBuilder.Built [ item ] -> item.SettlementId
                | other -> failwithf "Expected one settlement, got %A" other

            do!
                execute
                    "update settlements set status = 'ready', approved_at = @approved_at where tenant_id = @tenant_id and id = @settlement_id"
                    [ "tenant_id", acmeTenantId :> obj
                      "settlement_id", settlementId :> obj
                      "approved_at", DateTimeOffset.Parse "2026-06-01T09:00:00Z" :> obj ]

            let! posted =
                SettlementPoster.postReadySettlement
                    fixture.DataSource
                    acmeScope
                    { InvoiceReconEnabled = true
                      LocalPdfDirectory = None }
                    settlementId
                    (DateTimeOffset.Parse "2026-06-01T09:01:00Z")

            match posted with
            | SettlementPoster.EnqueuedInvoiceRecon _ -> ()
            | other -> failwithf "Expected Invoice Recon outbox enqueue, got %A" other

            use invoiceHttp =
                new HttpClient(new FakeInvoiceHandler(), BaseAddress = Uri "https://invoice-recon.fake")

            let hubEvents = ResizeArray<string>()

            let! result =
                OutboxProcessor.processDue
                    fixture.DataSource
                    { BatchSize = 10
                      MaxAttempts = 3
                      LeaseSeconds = 30
                      RetryDelaySeconds = 1 }
                    (EcosystemOutboxDispatcher.fakeStagingHandler invoiceHttp (fun eventType ->
                        hubEvents.Add eventType
                        Task.FromResult(Ok())))
                    (DateTimeOffset.Parse "2026-06-01T09:02:00Z")

            result.Done |> should equal 1
            hubEvents |> Seq.contains "settlement.posted" |> should equal true

            let! doneCount =
                scalarInt64
                    "select count(*)::bigint from outbox where tenant_id = @tenant_id and status = 'done' and op = 'invoice_recon.post_credit_note'"
                    [ "tenant_id", acmeTenantId :> obj ]

            doneCount |> should be (greaterThanOrEqualTo 1L)
        }
