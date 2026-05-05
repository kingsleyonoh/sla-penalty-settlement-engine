namespace Slapen.Ecosystem

open System.Net.Http
open System.Threading.Tasks

type HubEvent =
    { EventType: string
      EventId: string
      PayloadJson: string }

type HubClient = private { Http: EcosystemHttpClient }

[<RequireQualifiedAccess>]
module HubClient =
    let create httpClient config =
        { Http = EcosystemHttpClient.create httpClient config }

    let private eventJson event =
        $"{{\"event_type\":\"{event.EventType}\",\"event_id\":\"{event.EventId}\",\"payload\":{event.PayloadJson}}}"

    let emit client event : Task<Result<unit, string>> =
        task {
            let! result = EcosystemHttpClient.sendJson client.Http HttpMethod.Post "/api/events" None (eventJson event)

            return Result.map ignore result
        }
