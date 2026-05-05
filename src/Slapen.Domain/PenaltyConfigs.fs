namespace Slapen.Domain

[<RequireQualifiedAccess>]
module PenaltyConfigs =
    let private requirePositiveMoney name amount =
        if Money.cents amount <= 0L then
            Error(InvalidPenaltyConfig(sprintf "%s must be greater than zero" name))
        else
            Ok amount

    let private validateTierCurrency expected (tier: PenaltyTier) =
        if Money.currencyCode tier.Amount <> expected then
            Error(InvalidPenaltyConfig "tier currency must match config currency")
        else
            Ok tier

    let private validateTierOrder (tiers: PenaltyTier list) =
        let rec loop expectedMin (remaining: PenaltyTier list) =
            match remaining with
            | [] -> Ok()
            | tier :: rest when tier.MinBreaches <> expectedMin ->
                Error(InvalidPenaltyConfig "tiered tiers must be contiguous and non-overlapping")
            | tier :: rest ->
                match tier.MaxBreaches with
                | Some maxBreaches when maxBreaches < tier.MinBreaches ->
                    Error(InvalidPenaltyConfig "tier max_breaches must be >= min_breaches")
                | Some maxBreaches -> loop (maxBreaches + 1) rest
                | None when List.isEmpty rest -> Ok()
                | None -> Error(InvalidPenaltyConfig "open-ended tier must be last")

        loop 1 tiers

    let flatPerBreach amount =
        requirePositiveMoney "flat amount" amount |> Result.map FlatPerBreach

    let percentOfMonthlyFee percent monthlyFee =
        if percent <= 0.0M then
            Error(InvalidPenaltyConfig "percent must be greater than zero")
        else
            requirePositiveMoney "monthly fee" monthlyFee
            |> Result.map (fun fee -> PercentOfMonthlyFee(percent, fee))

    let tier minBreaches maxBreaches amount =
        if minBreaches < 1 then
            Error(InvalidPenaltyConfig "tier min_breaches must be >= 1")
        elif maxBreaches |> Option.exists (fun maximum -> maximum < minBreaches) then
            Error(InvalidPenaltyConfig "tier max_breaches must be >= min_breaches")
        else
            requirePositiveMoney "tier amount" amount
            |> Result.map (fun validated ->
                { MinBreaches = minBreaches
                  MaxBreaches = maxBreaches
                  Amount = validated })

    let tiered currency tiers =
        if List.isEmpty tiers then
            Error(InvalidPenaltyConfig "tiered config requires at least one tier")
        else
            tiers
            |> List.map (validateTierCurrency currency)
            |> List.fold
                (fun state result ->
                    match state, result with
                    | Error error, _ -> Error error
                    | _, Error error -> Error error
                    | Ok values, Ok value -> Ok(value :: values))
                (Ok [])
            |> Result.bind (fun _ -> validateTierOrder tiers)
            |> Result.map (fun _ -> Tiered(currency, tiers))

    let compoundingDaily dailyAmount maxDays =
        if maxDays < 1 then
            Error(InvalidPenaltyConfig "max_days must be >= 1")
        else
            requirePositiveMoney "daily amount" dailyAmount
            |> Result.map (fun amount -> CompoundingDaily(amount, maxDays))

    let linearPerUnitMissed amountPerUnit unitLabel =
        if System.String.IsNullOrWhiteSpace unitLabel then
            Error(InvalidPenaltyConfig "unit_label is required")
        else
            requirePositiveMoney "amount per unit" amountPerUnit
            |> Result.map (fun amount -> LinearPerUnitMissed(amount, unitLabel))
