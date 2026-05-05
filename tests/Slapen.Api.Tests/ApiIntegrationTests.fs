namespace Slapen.Api.Tests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Json
open System.Text.Json
open Xunit

type ApiIntegrationTests(fixture: PostgresFixture) =
    let createClient () : HttpClient * ApiFactory =
        let factory = new ApiFactory(fixture)
        factory.CreateClient(), factory

    let authorizedClient (key: string) =
        let client, factory = createClient ()
        client.DefaultRequestHeaders.Add("X-API-Key", key)
        client, factory

    let readJson (response: HttpResponseMessage) =
        task {
            let! body = response.Content.ReadAsStringAsync()
            return JsonDocument.Parse(body)
        }

    [<Fact>]
    member _.``health is public and emits a request id``() =
        task {
            let client, factory = createClient ()

            use! response = client.GetAsync("/api/health")

            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            Assert.True(response.Headers.Contains("X-Request-ID"))
            factory.Dispose()
        }

    [<Fact>]
    member _.``protected routes reject missing and invalid API keys``() =
        task {
            let client, factory = createClient ()

            use! missing = client.GetAsync("/api/tenants/me")
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode)

            client.DefaultRequestHeaders.Add("X-API-Key", "slapen_invalid_placeholder")
            use! invalid = client.GetAsync("/api/tenants/me")
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode)
            factory.Dispose()
        }

    [<Fact>]
    member _.``tenant me resolves API key without trusting request identity``() =
        task {
            let client, factory = authorizedClient TestKeys.tenantA

            use! response = client.GetAsync("/api/tenants/me")
            use! document = readJson response

            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            Assert.Equal("acme-gmbh-de", document.RootElement.GetProperty("slug").GetString())
            Assert.Equal("EUR", document.RootElement.GetProperty("defaultCurrency").GetString())
            factory.Dispose()
        }

    [<Fact>]
    member _.``tenant scoped contract detail returns not found across tenants``() =
        task {
            let tenantA, factoryA = authorizedClient TestKeys.tenantA
            let tenantB, factoryB = authorizedClient TestKeys.tenantB
            let tenantAContract = "/api/contracts/12000000-0000-0000-0000-000000000001"

            use! ownResponse = tenantA.GetAsync(tenantAContract)
            Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode)

            use! crossTenantResponse = tenantB.GetAsync(tenantAContract)
            Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode)
            factoryA.Dispose()
            factoryB.Dispose()
        }

    [<Fact>]
    member _.``manual breach can accrue reverse and expose append only ledger rows``() =
        task {
            let client, factory = authorizedClient TestKeys.tenantA

            let manualBreach =
                {| contractId = Guid.Parse("12000000-0000-0000-0000-000000000001")
                   slaClauseId = Guid.Parse("13000000-0000-0000-0000-000000000001")
                   sourceRef = $"api-test-{Guid.NewGuid():N}"
                   metricValue = 88.0M
                   unitsMissed = Nullable<decimal>()
                   observedAt = DateTimeOffset.Parse("2026-05-03T09:00:00Z")
                   reportedAt = DateTimeOffset.Parse("2026-05-03T10:00:00Z") |}

            use! created = client.PostAsJsonAsync("/api/breaches/manual", manualBreach)
            use! createdJson = readJson created
            Assert.Equal(HttpStatusCode.Created, created.StatusCode)
            let breachId = createdJson.RootElement.GetProperty("id").GetGuid()

            use! accrued = client.PostAsync($"/api/breaches/{breachId}/accrue", null)
            use! accruedJson = readJson accrued
            Assert.Equal(HttpStatusCode.OK, accrued.StatusCode)
            Assert.Equal("accrued", accruedJson.RootElement.GetProperty("status").GetString())
            Assert.Equal(2, accruedJson.RootElement.GetProperty("ledgerEntryIds").GetArrayLength())

            use! reversed =
                client.PostAsJsonAsync($"/api/breaches/{breachId}/reverse", {| reasonNotes = "withdrawn by operator" |})

            use! reversedJson = readJson reversed
            Assert.Equal(HttpStatusCode.OK, reversed.StatusCode)
            Assert.Equal("withdrawn", reversedJson.RootElement.GetProperty("status").GetString())
            Assert.Equal(2, reversedJson.RootElement.GetProperty("ledgerEntryIds").GetArrayLength())

            use! ledger = client.GetAsync($"/api/ledger/breaches/{breachId}")
            use! ledgerJson = readJson ledger
            Assert.Equal(HttpStatusCode.OK, ledger.StatusCode)
            Assert.Equal(4, ledgerJson.RootElement.GetProperty("items").GetArrayLength())
            factory.Dispose()
        }

    [<Fact>]
    member _.``tenant scoped breach ledger returns not found across tenants``() =
        task {
            let tenantA, factoryA = authorizedClient TestKeys.tenantA
            let tenantB, factoryB = authorizedClient TestKeys.tenantB

            let manualBreach =
                {| contractId = Guid.Parse("12000000-0000-0000-0000-000000000001")
                   slaClauseId = Guid.Parse("13000000-0000-0000-0000-000000000001")
                   sourceRef = $"cross-tenant-ledger-{Guid.NewGuid():N}"
                   metricValue = 87.0M
                   unitsMissed = Nullable<decimal>()
                   observedAt = DateTimeOffset.Parse("2026-05-04T09:00:00Z")
                   reportedAt = DateTimeOffset.Parse("2026-05-04T10:00:00Z") |}

            use! created = tenantA.PostAsJsonAsync("/api/breaches/manual", manualBreach)
            use! createdJson = readJson created
            Assert.Equal(HttpStatusCode.Created, created.StatusCode)
            let breachId = createdJson.RootElement.GetProperty("id").GetGuid()

            use! ownLedger = tenantA.GetAsync($"/api/ledger/breaches/{breachId}")
            Assert.Equal(HttpStatusCode.OK, ownLedger.StatusCode)

            use! crossTenantLedger = tenantB.GetAsync($"/api/ledger/breaches/{breachId}")
            Assert.Equal(HttpStatusCode.NotFound, crossTenantLedger.StatusCode)

            factoryA.Dispose()
            factoryB.Dispose()
        }

    interface IClassFixture<PostgresFixture>
