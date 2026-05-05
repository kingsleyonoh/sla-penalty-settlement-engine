namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open DotNet.Testcontainers.Builders
open DotNet.Testcontainers.Containers
open FsUnit.Xunit
open Slapen.Ecosystem
open Xunit

type NatsIntegrationTests() =
    let container: IContainer =
        ContainerBuilder("nats:2.10-alpine")
            .WithImage("nats:2.10-alpine")
            .WithPortBinding(4222, true)
            .WithCommand("-js")
            .Build()

    interface IAsyncLifetime with
        member _.InitializeAsync() =
            ValueTask(
                task {
                    do! container.StartAsync()
                    do! Task.Delay 500
                }
            )

        member _.DisposeAsync() =
            ValueTask(container.DisposeAsync().AsTask())

    [<Fact>]
    member _.``raw NATS boundary consumes staged Contract Lifecycle events``() : Task =
        task {
            let url = $"nats://127.0.0.1:{container.GetMappedPublicPort(4222)}"

            let connection =
                NatsConnection.create
                    { Url = url
                      CredentialsPath = None
                      StreamName = "ECOSYSTEM_EVENTS"
                      DurableName = "slapen_obligation_consumer"
                      Enabled = true }

            let payload =
                """{"event_id":"nats-it-001","contract_ref":"contract-acme-001","clause_ref":"Schedule B 2.3.1","metric_value":88,"observed_at":"2026-05-05T00:00:00Z","reported_at":"2026-05-05T00:00:01Z"}"""

            let consume =
                NatsConnection.consumeOnce connection "contract.obligation.breached" (TimeSpan.FromSeconds 5.0)

            do! Task.Delay 250
            do! NatsConnection.publish connection "contract.obligation.breached" payload
            let! message = consume

            message |> should equal (Some payload)
        }
