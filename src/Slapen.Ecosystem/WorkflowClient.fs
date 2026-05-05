namespace Slapen.Ecosystem

open System.Net.Http
open System.Threading.Tasks

type WorkflowClient = private { Http: EcosystemHttpClient }

[<RequireQualifiedAccess>]
module WorkflowClient =
    let create httpClient config =
        { Http = EcosystemHttpClient.create httpClient config }

    let triggerDisputeEscalation client idempotencyKey payloadJson : Task<Result<unit, string>> =
        task {
            let! result =
                EcosystemHttpClient.sendJson
                    client.Http
                    HttpMethod.Post
                    "/api/workflows/dispute-escalation/runs"
                    (Some idempotencyKey)
                    payloadJson

            return Result.map ignore result
        }
