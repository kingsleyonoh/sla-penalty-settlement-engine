namespace Slapen.Domain.Tests

open Slapen.Domain
open Xunit

module TestValues =
    let expectOk result =
        match result with
        | Ok value -> value
        | Error error -> failwithf "Expected Ok but got %A" error

    let money cents currency = Money.create cents currency |> expectOk

[<AutoOpen>]
module TypeAssertions =
    let assertMoney cents currency actual =
        Assert.Equal(cents, Money.cents actual)
        Assert.Equal(currency, Money.currency actual)

module MoneyTests =
    [<Fact>]
    let ``same-currency money addition is deterministic`` () =
        let first = TestValues.money 1250L "EUR"
        let second = TestValues.money 375L "EUR"

        let result = Money.add first second

        match result with
        | Ok amount -> assertMoney 1625L "EUR" amount
        | Error error -> failwithf "Expected same-currency addition to succeed but got %A" error

    [<Fact>]
    let ``money addition rejects mixed currencies`` () =
        let first = TestValues.money 1250L "EUR"
        let second = TestValues.money 375L "USD"

        let result = Money.add first second

        Assert.Equal<Result<Money, DomainError>>(Error(CurrencyMismatch("EUR", "USD")), result)

    [<Theory>]
    [<InlineData(-1L, "EUR")>]
    [<InlineData(100L, "eur")>]
    [<InlineData(100L, "EURO")>]
    let ``money rejects invalid primitive values`` cents currency =
        let result = Money.create cents currency

        Assert.True(Result.isError result)

module PenaltyConfigTests =
    [<Fact>]
    let ``tiered config rejects overlapping tiers`` () =
        let currency = CurrencyCode.create "EUR" |> TestValues.expectOk
        let tierOne = PenaltyConfigs.tier 1 (Some 3) (TestValues.money 50000L "EUR")
        let tierTwo = PenaltyConfigs.tier 3 None (TestValues.money 150000L "EUR")

        let result =
            Result.bind
                (fun first -> Result.bind (fun second -> PenaltyConfigs.tiered currency [ first; second ]) tierTwo)
                tierOne

        Assert.Equal<Result<PenaltyConfig, DomainError>>(
            Error(InvalidPenaltyConfig "tiered tiers must be contiguous and non-overlapping"),
            result
        )

    [<Fact>]
    let ``percent config rejects non-positive percentages`` () =
        let result =
            PenaltyConfigs.percentOfMonthlyFee 0.0M (TestValues.money 1000000L "EUR")

        Assert.Equal<Result<PenaltyConfig, DomainError>>(
            Error(InvalidPenaltyConfig "percent must be greater than zero"),
            result
        )
