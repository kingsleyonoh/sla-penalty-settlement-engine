namespace Slapen.Jobs

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Application
open Slapen.Data
open Slapen.Ecosystem

[<RequireQualifiedAccess>]
module ContractLifecycleRestBackfillJob =
    let private toInput (breach: ContractLifecycleBreach) : ExternalBreachInput =
        { SourceRef = breach.SourceRef
          ContractRef = breach.ContractRef
          ClauseRef = breach.ClauseRef
          MetricValue = breach.MetricValue
          UnitsMissed = breach.UnitsMissed
          ObservedAt = breach.ObservedAt
          ReportedAt = breach.ReportedAt
          RawPayloadJson = breach.RawPayloadJson }

    let execute
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (fetchBreaches: DateTimeOffset -> Task<ContractLifecycleBreach list>)
        (since: DateTimeOffset)
        : Task<IngestionResult> =
        task {
            let! breaches = fetchBreaches since
            let inputs = breaches |> List.map toInput
            return! Ingestion.ingestExternal dataSource scope "contract_lifecycle_rest" inputs
        }
