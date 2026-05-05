namespace Slapen.Domain

type CurrencyCode = string

type Money =
    { Cents: int64; Currency: CurrencyCode }

type DomainError =
    | InvalidMoneyAmount of cents: int64
    | InvalidCurrencyCode of currency: string
    | CurrencyMismatch of left: CurrencyCode * right: CurrencyCode

module Money =
    let private isCurrencyCode (currency: string) =
        not (isNull currency)
        && currency.Length = 3
        && currency |> Seq.forall (fun character -> character >= 'A' && character <= 'Z')

    let create cents currency =
        if cents < 0L then
            Error(InvalidMoneyAmount cents)
        elif not (isCurrencyCode currency) then
            Error(InvalidCurrencyCode currency)
        else
            Ok { Cents = cents; Currency = currency }

    let add left right =
        if left.Currency <> right.Currency then
            Error(CurrencyMismatch(left.Currency, right.Currency))
        else
            Ok
                { left with
                    Cents = left.Cents + right.Cents }
