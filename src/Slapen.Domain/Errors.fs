namespace Slapen.Domain

type DomainError =
    | InvalidMoneyAmount of cents: int64
    | InvalidCurrencyCode of currency: string
    | CurrencyMismatch of left: string * right: string
    | InvalidPenaltyConfig of reason: string
    | MissingRequiredMetric of metric: string
    | BreachBeforeContractActive
    | ClauseTenantMismatch
    | BreachClauseMismatch
    | LedgerAmountMustBePositive
    | PeriodInvalid
    | LedgerPairDirectionInvalid
    | LedgerPairKindInvalid
    | LedgerPairMismatch of reason: string
