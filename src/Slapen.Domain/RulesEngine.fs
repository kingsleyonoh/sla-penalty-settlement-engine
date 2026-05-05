namespace Slapen.Domain

open System

[<RequireQualifiedAccess>]
module RulesEngine =
    type private BasePenalty =
        { Amount: Money
          Detail: CapDetail option }

    let private roundCents (value: decimal) =
        Math.Round(value, 0, MidpointRounding.ToEven) |> int64

    let private elapsedDays (startAt: DateTimeOffset) (endAt: DateTimeOffset) =
        if endAt <= startAt then
            0
        else
            (endAt - startAt).TotalDays |> Math.Ceiling |> int

    let private accrualStart input =
        match input.Clause.AccrualStartFrom with
        | AccrualStartFrom.BreachObservedAt -> input.Breach.ObservedAt
        | AccrualStartFrom.BreachReportedAt -> input.Breach.ReportedAt
        | AccrualStartFrom.NextBillingPeriodStart ->
            DateTimeOffset(
                input.Breach.ObservedAt.Year,
                input.Breach.ObservedAt.Month,
                1,
                0,
                0,
                0,
                input.Breach.ObservedAt.Offset
            )
                .AddMonths(1)

    let private accrualEnd input =
        input.Breach.ResolvedAt |> Option.defaultValue input.AsOf

    let private priorCreditAccruals input =
        input.PreviousAccruals
        |> List.filter (fun entry ->
            entry.Direction = LedgerDirection.CreditOwedToUs
            && entry.SlaClauseId = input.Clause.Id)

    let private priorAccrued input currency =
        let entries = priorCreditAccruals input

        let mismatch =
            entries |> List.tryFind (fun entry -> Money.currency entry.Amount <> currency)

        match mismatch with
        | Some entry -> Error(CurrencyMismatch(Money.currency entry.Amount, currency))
        | None ->
            let total = entries |> List.sumBy (fun entry -> Money.cents entry.Amount)

            Money.create total currency

    let private priorAccruedCents input currency =
        priorAccrued input currency |> Result.map Money.cents

    let private contractCurrency input =
        input.Contract.Currency |> CurrencyCode.value

    let private requireSameCurrency input amount =
        let contractCurrency = contractCurrency input

        if Money.currency amount <> contractCurrency then
            Error(CurrencyMismatch(Money.currency amount, contractCurrency))
        else
            Ok amount

    let private validateContext input =
        let breachDate = DateOnly.FromDateTime(input.Breach.ObservedAt.UtcDateTime)

        if
            input.Contract.TenantId <> input.Clause.TenantId
            || input.Contract.TenantId <> input.Breach.TenantId
        then
            Error ClauseTenantMismatch
        elif
            input.Contract.Id <> input.Clause.ContractId
            || input.Contract.Id <> input.Breach.ContractId
            || input.Clause.Id <> input.Breach.SlaClauseId
        then
            Error BreachClauseMismatch
        elif breachDate < input.Contract.EffectiveDate then
            Error BreachBeforeContractActive
        else
            Ok()

    let private capDetail kind cap prior uncapped =
        { Kind = kind
          Cap = cap
          PriorAccrued = prior
          Uncapped = uncapped }

    let private applyCap kind prior amount cap =
        if Money.currency prior <> Money.currency cap then
            Error(CurrencyMismatch(Money.currency prior, Money.currency cap))
        elif Money.cents prior >= Money.cents cap then
            Ok(NoPenalty CapAlreadyReached)
        elif Money.cents prior + Money.cents amount > Money.cents cap then
            let capped = Money.ofSameCurrency (Money.cents cap - Money.cents prior) amount

            capped
            |> Result.map (fun cappedAmount -> CapApplied(cappedAmount, capDetail kind cap prior amount))
        else
            Ok(Accrued amount)

    let private applyConfiguredCaps input basePenalty =
        let prior = priorAccrued input (Money.currency basePenalty.Amount)

        match prior with
        | Error error -> Error error
        | Ok priorAmount ->
            let caps =
                [ input.Clause.CapPerPeriod |> Option.map (fun cap -> PeriodCap, cap)
                  input.Clause.CapPerContract |> Option.map (fun cap -> ContractCap, cap) ]
                |> List.choose id
                |> List.sortBy (fun (_, cap) -> Money.cents cap)

            match caps with
            | [] ->
                match basePenalty.Detail with
                | Some detail -> Ok(CapApplied(basePenalty.Amount, detail))
                | None -> Ok(Accrued basePenalty.Amount)
            | (kind, cap) :: _ -> applyCap kind priorAmount basePenalty.Amount cap

    let private moneyFrom amount cents = Money.ofSameCurrency cents amount

    let private percentOfMonthlyFee input percent monthlyFee =
        requireSameCurrency input monthlyFee
        |> Result.bind (fun fee ->
            let startAt = accrualStart input
            let days = elapsedDays startAt (accrualEnd input)
            let daysInMonth = DateTime.DaysInMonth(startAt.Year, startAt.Month)

            let cents =
                decimal (Money.cents fee) * percent / 100.0M * decimal days
                / decimal daysInMonth

            moneyFrom fee (roundCents cents))

    let private tiered input tiers =
        let breachCount = input.PriorMeasurementWindowBreachCount + 1

        let selected =
            tiers
            |> List.tryFind (fun tier ->
                breachCount >= tier.MinBreaches
                && (tier.MaxBreaches |> Option.forall (fun maximum -> breachCount <= maximum)))
            |> Option.defaultValue (List.last tiers)

        priorAccruedCents input (Money.currency selected.Amount)
        |> Result.bind (fun priorTotal ->
            if priorTotal >= Money.cents selected.Amount then
                Ok(NoPenalty NoAdditionalPenalty)
            else
                Money.ofSameCurrency (Money.cents selected.Amount - priorTotal) selected.Amount
                |> Result.map Accrued)

    let private compoundingDaily input dailyAmount maxDays =
        requireSameCurrency input dailyAmount
        |> Result.bind (fun amount ->
            let days = elapsedDays (accrualStart input) (accrualEnd input)
            let effectiveDays = min days maxDays
            let cappedTotal = Money.cents amount * int64 effectiveDays
            let maxTotal = Money.cents amount * int64 maxDays

            priorAccruedCents input (Money.currency amount)
            |> Result.bind (fun priorTotal ->
                if priorTotal >= maxTotal then
                    Ok(NoPenalty CapAlreadyReached)
                else
                    let incremental = max 0L (cappedTotal - priorTotal)

                    moneyFrom amount incremental
                    |> Result.map (fun accrued ->
                        if days >= maxDays then
                            let maxCap = Money.ofSameCurrency maxTotal amount |> Result.defaultValue amount
                            let prior = Money.ofSameCurrency priorTotal amount |> Result.defaultValue amount

                            CapApplied(accrued, capDetail PenaltyTypeMaxDays maxCap prior accrued)
                        elif incremental = 0L then
                            NoPenalty NoAdditionalPenalty
                        else
                            Accrued accrued)))

    let private linear units amountPerUnit =
        match units with
        | None -> Error(MissingRequiredMetric "units_missed")
        | Some unitsMissed ->
            let cents = decimal (Money.cents amountPerUnit) * unitsMissed
            moneyFrom amountPerUnit (roundCents cents)

    let private calculateBase input =
        match input.Clause.PenaltyConfig with
        | FlatPerBreach amount -> requireSameCurrency input amount |> Result.map Accrued
        | PercentOfMonthlyFee(percent, monthlyFee) -> percentOfMonthlyFee input percent monthlyFee |> Result.map Accrued
        | Tiered(_, tiers) -> tiered input tiers
        | CompoundingDaily(dailyAmount, maxDays) -> compoundingDaily input dailyAmount maxDays
        | LinearPerUnitMissed(amountPerUnit, _) ->
            requireSameCurrency input amountPerUnit
            |> Result.bind (fun amount -> linear input.Breach.UnitsMissed amount |> Result.map Accrued)

    let private toBasePenalty result =
        match result with
        | Accrued amount -> Some { Amount = amount; Detail = None }
        | CapApplied(amount, detail) ->
            Some
                { Amount = amount
                  Detail = Some detail }
        | NoPenalty _ -> None

    let calculatePenalty input =
        validateContext input
        |> Result.bind (fun _ ->
            if not input.Clause.Active then
                Ok(NoPenalty ClauseInactive)
            else
                calculateBase input)
        |> Result.bind (fun result ->
            match toBasePenalty result with
            | None -> Ok result
            | Some basePenalty when Money.cents basePenalty.Amount = 0L -> Ok(NoPenalty ZeroPenalty)
            | Some basePenalty -> applyConfiguredCaps input basePenalty)
