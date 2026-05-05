namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Slapen.Ecosystem
open Xunit

type EcosystemStubTests() =
    [<Fact>]
    member _.``contract lifecycle client is disabled stub without outbound calls``() : Task =
        task {
            let client =
                ContractLifecycleClient.create
                    { BaseUrl = None
                      ApiKey = None
                      Enabled = false }

            let! breaches = ContractLifecycleClient.fetchBreaches client (DateTimeOffset.Parse "2026-05-05T00:00:00Z")

            Assert.Empty breaches
            ContractLifecycleClient.isEnabled client |> should equal false
        }

    [<Fact>]
    member _.``nats connection is disabled stub until phase three``() : Task =
        task {
            let connection =
                NatsConnection.create
                    { Url = "nats://localhost:4222"
                      CredentialsPath = None
                      Enabled = false }

            let! connected = NatsConnection.connect connection

            connected |> should equal NatsConnection.ConnectionResult.Disabled
        }
