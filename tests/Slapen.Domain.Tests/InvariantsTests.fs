namespace Slapen.Domain.Tests

open System
open Slapen.Domain
open Xunit

module LedgerFactory =
    let tenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let contractId = Guid.Parse "12000000-0000-0000-0000-000000000001"
    let counterpartyId = Guid.Parse "11000000-0000-0000-0000-000000000001"
    let clauseId = Guid.Parse "13000000-0000-0000-0000-000000000001"
    let breachId = Guid.Parse "14000000-0000-0000-0000-000000000001"
    let periodStart = DateTimeOffset.Parse "2026-05-01T00:00:00Z"
    let periodEnd = DateTimeOffset.Parse "2026-05-31T23:59:59Z"
    let createdAt = DateTimeOffset.Parse "2026-05-05T10:00:00Z"

    let candidate direction amount =
        { Id = Guid.NewGuid()
          TenantId = tenantId
          ContractId = contractId
          CounterpartyId = counterpartyId
          SlaClauseId = clauseId
          BreachEventId = breachId
          EntryKind = LedgerEntryKind.Accrual
          Direction = direction
          Amount = amount
          AccrualPeriodStart = periodStart
          AccrualPeriodEnd = periodEnd
          CompensatesLedgerId = None
          ReasonCode = ReasonCode.SlaBreach
          ReasonNotes = None
          CreatedAt = createdAt
          CreatedBy = CreatedBy.System }

module LedgerPairTests =
    [<Fact>]
    let ``ledger pair creates bilateral credit and mirror entries`` () =
        let amount = TestValues.money 50000L "EUR"
        let credit = LedgerFactory.candidate LedgerDirection.CreditOwedToUs amount
        let mirror = LedgerFactory.candidate LedgerDirection.Mirror amount

        let result = LedgerPair.create credit mirror

        match result with
        | Ok pair ->
            let entries = LedgerPair.entries pair
            Assert.Equal(2, entries.Length)
            Assert.Contains(entries, fun entry -> entry.Direction = LedgerDirection.CreditOwedToUs)
            Assert.Contains(entries, fun entry -> entry.Direction = LedgerDirection.Mirror)
        | Error error -> failwithf "Expected valid ledger pair but got %A" error

    [<Fact>]
    let ``ledger pair rejects mismatched amounts`` () =
        let credit =
            LedgerFactory.candidate LedgerDirection.CreditOwedToUs (TestValues.money 50000L "EUR")

        let mirror =
            LedgerFactory.candidate LedgerDirection.Mirror (TestValues.money 49999L "EUR")

        let result = LedgerPair.create credit mirror

        Assert.Equal<Result<LedgerPair, DomainError>>(Error(LedgerPairMismatch "amount must match"), result)

    [<Fact>]
    let ``ledger pair rejects two credit sides`` () =
        let amount = TestValues.money 50000L "EUR"
        let left = LedgerFactory.candidate LedgerDirection.CreditOwedToUs amount
        let right = LedgerFactory.candidate LedgerDirection.CreditOwedToUs amount

        let result = LedgerPair.create left right

        Assert.Equal<Result<LedgerPair, DomainError>>(Error LedgerPairDirectionInvalid, result)
