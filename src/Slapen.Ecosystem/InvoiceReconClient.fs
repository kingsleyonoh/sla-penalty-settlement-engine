namespace Slapen.Ecosystem

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks

type InvoiceReconClient = private { Http: EcosystemHttpClient }

[<RequireQualifiedAccess>]
module InvoiceReconClient =
    let create httpClient config =
        { Http = EcosystemHttpClient.create httpClient config }

    let private parseId body =
        if String.IsNullOrWhiteSpace body then
            ""
        else
            use document = JsonDocument.Parse body
            let mutable id = Unchecked.defaultof<JsonElement>

            if document.RootElement.TryGetProperty("id", &id) then
                id.GetString()
            else
                body

    let postCreditNote client idempotencyKey payloadJson : Task<Result<string, string>> =
        task {
            let! result =
                EcosystemHttpClient.sendJson
                    client.Http
                    HttpMethod.Post
                    "/api/invoices"
                    (Some idempotencyKey)
                    payloadJson

            return Result.map parseId result
        }

    let postDebitMemo client idempotencyKey payloadJson : Task<Result<string, string>> =
        task {
            let! result =
                EcosystemHttpClient.sendJson
                    client.Http
                    HttpMethod.Post
                    "/api/debit-memos"
                    (Some idempotencyKey)
                    payloadJson

            return Result.map parseId result
        }
