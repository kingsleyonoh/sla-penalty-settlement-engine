namespace Slapen.Jobs

open System.Threading.Tasks
open Npgsql
open Slapen.Application
open Slapen.Data
open Slapen.Ecosystem

[<RequireQualifiedAccess>]
module ContractLifecycleNatsConsumer =
    let private parse payload : ExternalBreachInput =
        let breach = ContractLifecycleClient.parseBreach payload

        { SourceRef = breach.SourceRef
          ContractRef = breach.ContractRef
          ClauseRef = breach.ClauseRef
          MetricValue = breach.MetricValue
          UnitsMissed = breach.UnitsMissed
          ObservedAt = breach.ObservedAt
          ReportedAt = breach.ReportedAt
          RawPayloadJson = payload }

    let handleMessage (dataSource: NpgsqlDataSource) (scope: TenantScope) (payload: string) : Task<IngestionResult> =
        task {
            let input = parse payload
            return! Ingestion.ingestExternal dataSource scope "contract_lifecycle_nats" [ input ]
        }

    let consumeOnce
        (connection: NatsConnection)
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (subject: string)
        timeout
        : Task<IngestionResult option> =
        task {
            let! message = NatsConnection.consumeOnce connection subject timeout

            match message with
            | None -> return None
            | Some payload ->
                let! result = handleMessage dataSource scope payload
                return Some result
        }
