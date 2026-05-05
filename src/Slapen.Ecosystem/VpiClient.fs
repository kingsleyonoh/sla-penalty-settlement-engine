namespace Slapen.Ecosystem

open System.Net.Http
open System.Threading.Tasks

type VpiClient = private { Http: EcosystemHttpClient }

[<RequireQualifiedAccess>]
module VpiClient =
    let create httpClient config =
        { Http = EcosystemHttpClient.create httpClient config }

    let emitSignal client payloadJson : Task<Result<unit, string>> =
        task {
            let! result = EcosystemHttpClient.sendJson client.Http HttpMethod.Post "/api/signals" None payloadJson
            return Result.map ignore result
        }
