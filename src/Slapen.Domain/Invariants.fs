namespace Slapen.Domain

type LedgerEntry =
    private
    | LedgerEntry of LedgerEntryCandidate

    member this.Id =
        let (LedgerEntry entry) = this
        entry.Id

    member this.Direction =
        let (LedgerEntry entry) = this
        entry.Direction

    member this.Amount =
        let (LedgerEntry entry) = this
        entry.Amount

    member this.Snapshot =
        let (LedgerEntry entry) = this
        entry

type LedgerPair =
    private
        { Credit: LedgerEntry
          Mirror: LedgerEntry }

[<RequireQualifiedAccess>]
module LedgerPair =
    let private sameContext credit mirror =
        credit.TenantId = mirror.TenantId
        && credit.ContractId = mirror.ContractId
        && credit.CounterpartyId = mirror.CounterpartyId
        && credit.SlaClauseId = mirror.SlaClauseId
        && credit.BreachEventId = mirror.BreachEventId

    let private validate credit mirror =
        if Money.cents credit.Amount <= 0L || Money.cents mirror.Amount <= 0L then
            Error LedgerAmountMustBePositive
        elif
            credit.Direction <> LedgerDirection.CreditOwedToUs
            || mirror.Direction <> LedgerDirection.Mirror
        then
            Error LedgerPairDirectionInvalid
        elif credit.EntryKind <> mirror.EntryKind then
            Error LedgerPairKindInvalid
        elif credit.Amount <> mirror.Amount then
            Error(LedgerPairMismatch "amount must match")
        elif
            credit.AccrualPeriodStart <> mirror.AccrualPeriodStart
            || credit.AccrualPeriodEnd <> mirror.AccrualPeriodEnd
        then
            Error(LedgerPairMismatch "period must match")
        elif not (sameContext credit mirror) then
            Error(LedgerPairMismatch "tenant contract counterparty clause breach context must match")
        elif credit.CompensatesLedgerId <> mirror.CompensatesLedgerId then
            Error(LedgerPairMismatch "compensating ledger reference must match")
        elif credit.AccrualPeriodEnd < credit.AccrualPeriodStart then
            Error PeriodInvalid
        else
            Ok()

    let create credit mirror =
        validate credit mirror
        |> Result.map (fun _ ->
            { Credit = LedgerEntry credit
              Mirror = LedgerEntry mirror })

    let entries pair = [ pair.Credit; pair.Mirror ]
