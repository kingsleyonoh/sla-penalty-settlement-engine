namespace Slapen.Ecosystem

open System
open System.Threading.Tasks

type ContractLifecycleBreach =
    { SourceRef: string
      ContractRef: string
      ClauseRef: string
      MetricValue: decimal
      UnitsMissed: decimal option
      ObservedAt: DateTimeOffset
      RawPayloadJson: string }

type ContractLifecycleClient = private { Http: EcosystemHttpClient }

[<RequireQualifiedAccess>]
module ContractLifecycleClient =
    let create config =
        { Http = EcosystemHttpClient.create config }

    let isEnabled client =
        EcosystemHttpClient.isEnabled client.Http

    let fetchBreaches (_client: ContractLifecycleClient) (_since: DateTimeOffset) : Task<ContractLifecycleBreach list> =
        Task.FromResult []
