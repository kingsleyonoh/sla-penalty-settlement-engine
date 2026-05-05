namespace Slapen.Domain

open System

[<Struct>]
type CurrencyCode = private CurrencyCode of string

[<RequireQualifiedAccess>]
module CurrencyCode =
    let private isValid (currency: string) =
        not (isNull currency)
        && currency.Length = 3
        && currency |> Seq.forall (fun character -> character >= 'A' && character <= 'Z')

    let create currency =
        if isValid currency then
            Ok(CurrencyCode currency)
        else
            Error(InvalidCurrencyCode currency)

    let value (CurrencyCode currency) = currency

[<Struct>]
type Money =
    private
        { CentsValue: int64
          CurrencyValue: CurrencyCode }

[<RequireQualifiedAccess>]
module Money =
    let create cents currency =
        if cents < 0L then
            Error(InvalidMoneyAmount cents)
        else
            CurrencyCode.create currency
            |> Result.map (fun currencyCode ->
                { CentsValue = cents
                  CurrencyValue = currencyCode })

    let cents amount = amount.CentsValue

    let currencyCode amount = amount.CurrencyValue

    let currency amount =
        amount.CurrencyValue |> CurrencyCode.value

    let add left right =
        if left.CurrencyValue <> right.CurrencyValue then
            Error(CurrencyMismatch(currency left, currency right))
        else
            Ok
                { left with
                    CentsValue = left.CentsValue + right.CentsValue }

    let subtract left right =
        if left.CurrencyValue <> right.CurrencyValue then
            Error(CurrencyMismatch(currency left, currency right))
        elif left.CentsValue < right.CentsValue then
            Error(InvalidMoneyAmount(left.CentsValue - right.CentsValue))
        else
            Ok
                { left with
                    CentsValue = left.CentsValue - right.CentsValue }

    let ofSameCurrency cents amount = create cents (currency amount)

type MeasurementWindow =
    | Daily
    | Weekly
    | Monthly
    | Quarterly
    | PerIncident

type AccrualStartFrom =
    | BreachObservedAt
    | BreachReportedAt
    | NextBillingPeriodStart

type ContractStatus =
    | Active
    | Expired
    | Terminated

type BreachStatus =
    | Pending
    | Accrued
    | Disputed
    | Withdrawn
    | Superseded

type LedgerEntryKind =
    | Accrual
    | Reversal
    | Adjustment

type LedgerDirection =
    | CreditOwedToUs
    | Mirror

type ReasonCode =
    | SlaBreach
    | DisputeResolvedInOurFavor
    | DisputeResolvedAgainst
    | OperatorCorrection
    | ContractCapApplied
    | WithdrawnBySource

type CreatedBy =
    | System
    | User of userId: Guid
    | Adapter

type PenaltyTier =
    { MinBreaches: int
      MaxBreaches: int option
      Amount: Money }

type PenaltyConfig =
    | FlatPerBreach of amount: Money
    | PercentOfMonthlyFee of percent: decimal * monthlyFee: Money
    | Tiered of currency: CurrencyCode * tiers: PenaltyTier list
    | CompoundingDaily of dailyAmount: Money * maxDays: int
    | LinearPerUnitMissed of amountPerUnit: Money * unitLabel: string

type Contract =
    { Id: Guid
      TenantId: Guid
      CounterpartyId: Guid
      Reference: string
      Currency: CurrencyCode
      EffectiveDate: DateOnly
      ExpiryDate: DateOnly option
      Status: ContractStatus }

type SlaClause =
    { Id: Guid
      TenantId: Guid
      ContractId: Guid
      Reference: string
      Metric: string
      MeasurementWindow: MeasurementWindow
      TargetValue: decimal
      PenaltyConfig: PenaltyConfig
      CapPerPeriod: Money option
      CapPerContract: Money option
      AccrualStartFrom: AccrualStartFrom
      Active: bool }

type BreachEvent =
    { Id: Guid
      TenantId: Guid
      ContractId: Guid
      SlaClauseId: Guid
      MetricValue: decimal
      UnitsMissed: decimal option
      ObservedAt: DateTimeOffset
      ReportedAt: DateTimeOffset
      ResolvedAt: DateTimeOffset option
      Status: BreachStatus }

type LedgerEntryCandidate =
    { Id: Guid
      TenantId: Guid
      ContractId: Guid
      CounterpartyId: Guid
      SlaClauseId: Guid
      BreachEventId: Guid
      EntryKind: LedgerEntryKind
      Direction: LedgerDirection
      Amount: Money
      AccrualPeriodStart: DateTimeOffset
      AccrualPeriodEnd: DateTimeOffset
      CompensatesLedgerId: Guid option
      ReasonCode: ReasonCode
      ReasonNotes: string option
      CreatedAt: DateTimeOffset
      CreatedBy: CreatedBy }

type NoPenaltyReason =
    | ClauseInactive
    | CapAlreadyReached
    | NoAdditionalPenalty
    | ZeroPenalty

type CapKind =
    | PeriodCap
    | ContractCap
    | PenaltyTypeMaxDays

type CapDetail =
    { Kind: CapKind
      Cap: Money
      PriorAccrued: Money
      Uncapped: Money }

type PenaltyResult =
    | Accrued of amount: Money
    | CapApplied of amount: Money * detail: CapDetail
    | NoPenalty of reason: NoPenaltyReason

type PenaltyCalculationInput =
    { Contract: Contract
      Clause: SlaClause
      Breach: BreachEvent
      PreviousAccruals: LedgerEntryCandidate list
      PriorMeasurementWindowBreachCount: int
      AsOf: DateTimeOffset }
