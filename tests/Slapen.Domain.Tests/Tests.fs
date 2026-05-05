module Slapen.Domain.Tests.MoneyTests

open Slapen.Domain
open Xunit

[<Fact>]
let ``same-currency money addition is deterministic`` () =
    let result =
        Money.create 1250L "EUR"
        |> Result.bind (fun first -> Money.create 375L "EUR" |> Result.bind (fun second -> Money.add first second))

    Assert.Equal<Result<Money, DomainError>>(Ok { Cents = 1625L; Currency = "EUR" }, result)

[<Fact>]
let ``money addition rejects mixed currencies`` () =
    let result =
        Money.create 1250L "EUR"
        |> Result.bind (fun first -> Money.create 375L "USD" |> Result.bind (fun second -> Money.add first second))

    Assert.Equal<Result<Money, DomainError>>(Error(CurrencyMismatch("EUR", "USD")), result)
