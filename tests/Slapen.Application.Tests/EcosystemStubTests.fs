namespace Slapen.Application.Tests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FsUnit.Xunit
open Slapen.Ecosystem
open Xunit

type private CapturingHandler(statusCode: HttpStatusCode, responseBody: string) =
    inherit HttpMessageHandler()

    let mutable request: HttpRequestMessage option = None
    let mutable content: string option = None

    member _.Request = request
    member _.Content = content

    override _.SendAsync(message, _cancellationToken) =
        task {
            request <- Some message

            let! body =
                if isNull message.Content then
                    Task.FromResult ""
                else
                    message.Content.ReadAsStringAsync()

            content <- Some body

            return
                new HttpResponseMessage(
                    statusCode,
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                )
        }

type EcosystemClientTests() =
    [<Fact>]
    member _.``notification hub client emits events with api key header``() : Task =
        task {
            let handler =
                new CapturingHandler(HttpStatusCode.Accepted, """{"status":"queued"}""")

            use http =
                new HttpClient(handler, BaseAddress = new Uri("https://hub.example.test"))

            let client =
                HubClient.create
                    http
                    { BaseUrl = Some "https://hub.example.test"
                      ApiKey = Some "fake_key"
                      Enabled = true }

            let! result =
                HubClient.emit
                    client
                    { EventType = "penalty.accrued"
                      EventId = "event-001"
                      PayloadJson = """{"amount_cents":50000}""" }

            Assert.True(Result.isOk result)
            handler.Request.Value.RequestUri.AbsolutePath |> should equal "/api/events"

            handler.Request.Value.Headers.GetValues("X-API-Key")
            |> Seq.head
            |> should equal "fake_key"

            handler.Content.Value.Contains("penalty.accrued") |> should equal true
        }

    [<Fact>]
    member _.``invoice recon client posts credit notes with idempotency key``() : Task =
        task {
            let handler =
                new CapturingHandler(HttpStatusCode.Created, """{"id":"ir-credit-001"}""")

            use http =
                new HttpClient(handler, BaseAddress = new Uri("https://invoice.example.test"))

            let client =
                InvoiceReconClient.create
                    http
                    { BaseUrl = Some "https://invoice.example.test"
                      ApiKey = Some "fake_key"
                      Enabled = true }

            let! result =
                InvoiceReconClient.postCreditNote
                    client
                    "idem-credit-001"
                    """{"settlement_id":"65000000-0000-0000-0000-000000000001"}"""

            Assert.Equal(Ok "ir-credit-001", result)
            handler.Request.Value.RequestUri.AbsolutePath |> should equal "/api/invoices"

            handler.Request.Value.Headers.GetValues("X-API-Key")
            |> Seq.head
            |> should equal "fake_key"

            handler.Request.Value.Headers.GetValues("Idempotency-Key")
            |> Seq.head
            |> should equal "idem-credit-001"
        }

    [<Fact>]
    member _.``contract lifecycle client parses REST backfill breaches``() : Task =
        task {
            let body =
                """
                {
                  "items": [
                    {
                      "event_id": "cl-event-001",
                      "contract_ref": "contract-acme-001",
                      "clause_ref": "Schedule B 2.3.1",
                      "metric_value": 88.5,
                      "units_missed": 2.0,
                      "observed_at": "2026-05-05T08:00:00Z",
                      "reported_at": "2026-05-05T08:01:00Z"
                    }
                  ]
                }
                """

            let handler = new CapturingHandler(HttpStatusCode.OK, body)

            use http =
                new HttpClient(handler, BaseAddress = new Uri("https://contracts.example.test"))

            let client =
                ContractLifecycleClient.create
                    http
                    { BaseUrl = Some "https://contracts.example.test"
                      ApiKey = Some "fake_key"
                      Enabled = true }

            let! breaches = ContractLifecycleClient.fetchBreaches client (DateTimeOffset.Parse "2026-05-05T00:00:00Z")

            breaches.Length |> should equal 1
            breaches.Head.SourceRef |> should equal "cl-event-001"
            breaches.Head.ContractRef |> should equal "contract-acme-001"
            handler.Request.Value.RequestUri.AbsolutePath |> should equal "/api/obligations"

            handler.Request.Value.RequestUri.Query.Contains("status=breached%2Coverdue")
            |> should equal true
        }

    [<Fact>]
    member _.``vpi and workflow clients can be disabled without outbound calls``() : Task =
        task {
            let handler = new CapturingHandler(HttpStatusCode.OK, "{}")

            use http =
                new HttpClient(handler, BaseAddress = new Uri("https://disabled.example.test"))

            let config =
                { BaseUrl = None
                  ApiKey = None
                  Enabled = false }

            let workflow = WorkflowClient.create http config
            let vpi = VpiClient.create http config

            let! workflowResult =
                WorkflowClient.triggerDisputeEscalation
                    workflow
                    "idem-workflow"
                    """{"breach_id":"65000000-0000-0000-0000-000000000001"}"""

            let! vpiResult =
                VpiClient.emitSignal
                    vpi
                    """{"signal_code":"supplier.penalty.accrued","vendor_external_ref":"vpi-acme-nordlicht","value_numeric":1,"observed_at":"2026-05-05T00:00:00Z","raw_context":{}}"""

            Assert.True(Result.isOk workflowResult)
            Assert.True(Result.isOk vpiResult)
            handler.Request |> should equal None
        }
