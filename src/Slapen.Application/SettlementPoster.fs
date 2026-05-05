namespace Slapen.Application

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Npgsql
open Slapen.Data
open Slapen.Templates

type SettlementPostingConfig =
    { InvoiceReconEnabled: bool
      LocalPdfDirectory: string option }

[<RequireQualifiedAccess>]
module SettlementPoster =
    type Outcome =
        | PostedLocalPdf of path: string
        | EnqueuedInvoiceRecon of outboxId: Guid
        | AlreadyPosted of path: string option
        | InvalidStatus of status: string
        | SnapshotMissing
        | NotFound

    let private localPdfPath directory settlementId =
        match directory with
        | None -> sprintf "local-pdf://settlement/%O.pdf" settlementId
        | Some directory ->
            Directory.CreateDirectory directory |> ignore
            Path.Combine(directory, sprintf "%O.pdf" settlementId)

    let private writeLocalPdf directory settlementId bytes =
        match directory with
        | None -> sprintf "local-pdf://settlement/%O.pdf" settlementId
        | Some _ ->
            let path = localPdfPath directory settlementId
            File.WriteAllBytes(path, bytes)
            path

    let private invoicePayload settlementId snapshot =
        JsonSerializer.Serialize(
            {| settlement_id = settlementId
               pdf_snapshot = snapshot |}
        )

    let private enqueueInvoiceRecon (dataSource: NpgsqlDataSource) scope settlementId snapshot now =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()
            use! transaction = connection.BeginTransactionAsync()

            try
                let! marked = SettlementPostingRepository.markPosting connection transaction scope settlementId

                if not marked then
                    do! transaction.RollbackAsync()
                    return None
                else
                    let message =
                        { Id = Guid.NewGuid()
                          Operation = "invoice_recon.post_credit_note"
                          PayloadJson = invoicePayload settlementId snapshot
                          IdempotencyKey = Some(sprintf "invoice-recon-credit-note-%O" settlementId)
                          NextRunAt = now }

                    let! outboxId = OutboxRepository.enqueueWithinTransaction connection transaction scope message
                    do! transaction.CommitAsync()
                    return Some outboxId
            with error ->
                do! transaction.RollbackAsync()
                return raise error
        }

    let postReadySettlement
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (config: SettlementPostingConfig)
        (settlementId: Guid)
        (now: DateTimeOffset)
        : Task<Outcome> =
        task {
            let! settlement = SettlementsRepository.findById dataSource scope settlementId

            match settlement with
            | None -> return NotFound
            | Some row when row.Status = "posted" -> return AlreadyPosted row.PdfUrl
            | Some row when row.Status <> "ready" && row.Status <> "failed" -> return InvalidStatus row.Status
            | Some row ->
                match row.PdfSnapshotJson with
                | None -> return SnapshotMissing
                | Some snapshotJson when config.InvoiceReconEnabled ->
                    let! outboxId = enqueueInvoiceRecon dataSource scope settlementId snapshotJson now

                    match outboxId with
                    | Some id -> return EnqueuedInvoiceRecon id
                    | None -> return InvalidStatus row.Status
                | Some snapshotJson ->
                    let snapshot = SettlementPdf.deserializeSnapshot snapshotJson
                    let bytes = PdfRenderer.renderSettlement snapshot
                    let path = writeLocalPdf config.LocalPdfDirectory settlementId bytes
                    let! posted = SettlementPostingRepository.markPostedLocal dataSource scope settlementId path now

                    match posted with
                    | true -> return PostedLocalPdf path
                    | false -> return InvalidStatus row.Status
        }
