namespace Slapen.Jobs

open System.Net.Http
open System.Text
open System.Threading.Tasks
open Slapen.Data

[<RequireQualifiedAccess>]
module EcosystemOutboxDispatcher =
    let fakeStagingHandler
        (invoiceRecon: HttpClient)
        (emitHubEvent: string -> Task<Result<unit, string>>)
        (message: OutboxMessage)
        : Task<Result<unit, string>> =
        task {
            match message.Operation with
            | "invoice_recon.post_credit_note" ->
                use content =
                    new StringContent(message.PayloadJson, Encoding.UTF8, "application/json")

                use! response = invoiceRecon.PostAsync("/api/invoices", content)

                if response.IsSuccessStatusCode then
                    return! emitHubEvent "settlement.posted"
                else
                    return Error $"invoice_recon_status_{int response.StatusCode}"
            | other -> return Error $"unsupported_outbox_operation:{other}"
        }
