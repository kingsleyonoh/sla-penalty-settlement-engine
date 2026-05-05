namespace Slapen.Application

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data
open Slapen.Domain
open Slapen.Templates
open Slapen.Tenants

type BuiltSettlement =
    { SettlementId: Guid
      AmountCents: int64
      Snapshot: SettlementSnapshot
      PdfBytes: byte array }

[<RequireQualifiedAccess>]
module SettlementBuilder =
    type Outcome =
        | Built of BuiltSettlement list
        | TenantNotFound
        | NothingToBuild

    let private createdByUserId =
        function
        | CreatedBy.User userId -> Some userId
        | CreatedBy.System
        | CreatedBy.Adapter -> None

    let private tenantBlock (snapshot: TenantSnapshot) =
        { TenantId = snapshot.TenantId
          LegalName = snapshot.LegalName
          FullLegalName = snapshot.FullLegalName
          DisplayName = snapshot.DisplayName
          AddressJson = snapshot.AddressJson
          RegistrationJson = snapshot.RegistrationJson
          ContactJson = snapshot.ContactJson
          Locale = snapshot.Locale
          Timezone = snapshot.Timezone
          CapturedAt = snapshot.CapturedAt }

    let private createSnapshot
        tenant
        settlementId
        periodStart
        periodEnd
        asOf
        (rows: UncommittedSettlementLedgerEntry list)
        =
        let head = rows |> List.head
        let amount = rows |> List.sumBy _.AmountCents

        { SettlementId = settlementId
          SettlementNumber = sprintf "SLAPEN-%s" (settlementId.ToString("N").Substring(0, 12).ToUpperInvariant())
          Tenant = tenantBlock tenant
          Counterparty =
            { CounterpartyId = head.CounterpartyId
              CanonicalName = head.CounterpartyName
              TaxId = head.CounterpartyTaxId
              CountryCode = head.CounterpartyCountryCode }
          Contract =
            { ContractId = head.ContractId
              Reference = head.ContractReference
              Title = head.ContractTitle }
          Currency = head.Currency
          AmountCents = amount
          PeriodStart = periodStart
          PeriodEnd = periodEnd
          CreatedAt = asOf
          Lines =
            rows
            |> List.map (fun row ->
                { LedgerEntryId = row.LedgerEntryId
                  BreachEventId = row.BreachEventId
                  ClauseReference = row.ClauseReference
                  AmountCents = row.AmountCents
                  Currency = row.Currency
                  PeriodStart = row.AccrualPeriodStart
                  PeriodEnd = row.AccrualPeriodEnd }) }

    let private insertBuilt
        (dataSource: NpgsqlDataSource)
        scope
        periodStart
        periodEnd
        createdBy
        asOf
        tenant
        (rows: UncommittedSettlementLedgerEntry list)
        =
        task {
            let settlementId = Guid.NewGuid()
            let snapshot = createSnapshot tenant settlementId periodStart periodEnd asOf rows
            let json = SettlementPdf.serializeSnapshot snapshot
            let pdfBytes = PdfRenderer.renderSettlement snapshot

            use! connection = dataSource.OpenConnectionAsync().AsTask()
            use! transaction = connection.BeginTransactionAsync()

            try
                let settlement =
                    { Id = settlementId
                      TenantId = TenantScope.value scope
                      CounterpartyId = snapshot.Counterparty.CounterpartyId
                      ContractId = snapshot.Contract.ContractId
                      Currency = snapshot.Currency
                      AmountCents = snapshot.AmountCents
                      PdfSnapshotJson = json
                      PeriodStart = periodStart
                      PeriodEnd = periodEnd
                      CreatedAt = asOf
                      CreatedByUserId = createdByUserId createdBy
                      LedgerEntryIds = rows |> List.map _.LedgerEntryId }

                let! _ = SettlementsRepository.insert connection transaction scope settlement
                do! transaction.CommitAsync()

                return
                    { SettlementId = settlementId
                      AmountCents = snapshot.AmountCents
                      Snapshot = snapshot
                      PdfBytes = pdfBytes }
            with error ->
                do! transaction.RollbackAsync()
                return raise error
        }

    let buildPending
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (periodStart: DateOnly)
        (periodEnd: DateOnly)
        (createdBy: CreatedBy)
        (asOf: DateTimeOffset)
        : Task<Outcome> =
        task {
            let! tenant = TenantSnapshotter.capture dataSource scope

            match tenant with
            | None -> return TenantNotFound
            | Some tenant ->
                let! rows = SettlementsRepository.listUncommittedAccruals dataSource scope periodStart periodEnd asOf

                if List.isEmpty rows then
                    return NothingToBuild
                else
                    let groups =
                        rows
                        |> List.groupBy (fun row -> row.CounterpartyId, row.ContractId, row.Currency)
                        |> List.map snd

                    let built = ResizeArray<BuiltSettlement>()

                    for group in groups do
                        let! item = insertBuilt dataSource scope periodStart periodEnd createdBy asOf tenant group
                        built.Add item

                    return Built(List.ofSeq built)
        }
