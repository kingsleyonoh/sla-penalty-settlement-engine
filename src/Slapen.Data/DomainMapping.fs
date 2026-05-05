namespace Slapen.Data

open System
open System.Text.Json
open Slapen.Domain

[<RequireQualifiedAccess>]
module DomainMapping =
    let ledgerEntryKindText =
        function
        | LedgerEntryKind.Accrual -> "accrual"
        | LedgerEntryKind.Reversal -> "reversal"
        | LedgerEntryKind.Adjustment -> "adjustment"

    let ledgerDirectionText =
        function
        | LedgerDirection.CreditOwedToUs -> "credit_owed_to_us"
        | LedgerDirection.Mirror -> "mirror"

    let reasonCodeText =
        function
        | ReasonCode.SlaBreach -> "sla_breach"
        | ReasonCode.DisputeResolvedInOurFavor -> "dispute_resolved_in_our_favor"
        | ReasonCode.DisputeResolvedAgainst -> "dispute_resolved_against"
        | ReasonCode.OperatorCorrection -> "operator_correction"
        | ReasonCode.ContractCapApplied -> "contract_cap_applied"
        | ReasonCode.WithdrawnBySource -> "withdrawn_by_source"

    let createdByParts =
        function
        | CreatedBy.System -> "system", None
        | CreatedBy.Adapter -> "adapter", None
        | CreatedBy.User userId -> "user", Some userId

    let measurementWindowText =
        function
        | MeasurementWindow.Daily -> "daily"
        | MeasurementWindow.Weekly -> "weekly"
        | MeasurementWindow.Monthly -> "monthly"
        | MeasurementWindow.Quarterly -> "quarterly"
        | MeasurementWindow.PerIncident -> "per_incident"

    let breachStatusText =
        function
        | BreachStatus.Pending -> "pending"
        | BreachStatus.Accrued -> "accrued"
        | BreachStatus.Disputed -> "disputed"
        | BreachStatus.Withdrawn -> "withdrawn"
        | BreachStatus.Superseded -> "superseded"

    let ledgerEntryKindFromText =
        function
        | "accrual" -> LedgerEntryKind.Accrual
        | "reversal" -> LedgerEntryKind.Reversal
        | "adjustment" -> LedgerEntryKind.Adjustment
        | value -> failwith $"Unknown ledger entry kind '{value}'."

    let ledgerDirectionFromText =
        function
        | "credit_owed_to_us" -> LedgerDirection.CreditOwedToUs
        | "mirror" -> LedgerDirection.Mirror
        | value -> failwith $"Unknown ledger direction '{value}'."

    let reasonCodeFromText =
        function
        | "sla_breach" -> ReasonCode.SlaBreach
        | "dispute_resolved_in_our_favor" -> ReasonCode.DisputeResolvedInOurFavor
        | "dispute_resolved_against" -> ReasonCode.DisputeResolvedAgainst
        | "operator_correction" -> ReasonCode.OperatorCorrection
        | "contract_cap_applied" -> ReasonCode.ContractCapApplied
        | "withdrawn_by_source" -> ReasonCode.WithdrawnBySource
        | value -> failwith $"Unknown reason code '{value}'."

    let createdByFromText kind (userId: Guid option) =
        match kind, userId with
        | "system", _ -> CreatedBy.System
        | "adapter", _ -> CreatedBy.Adapter
        | "user", Some id -> CreatedBy.User id
        | "user", None -> failwith "Ledger row created_by_kind=user requires created_by_user_id."
        | value, _ -> failwith $"Unknown created_by_kind '{value}'."

    let money (cents: int64) (currency: string) =
        match Money.create cents currency with
        | Ok amount -> amount
        | Error error -> failwithf "Invalid persisted money value: %A" error

    let private requireProperty (document: JsonDocument) (name: string) =
        let mutable value = Unchecked.defaultof<JsonElement>

        if document.RootElement.TryGetProperty(name, &value) then
            value
        else
            failwith $"Penalty config missing required property '{name}'."

    let private jsonMoney (document: JsonDocument) (amountProperty: string) =
        let cents = (requireProperty document amountProperty).GetInt64()
        let currency = (requireProperty document "currency").GetString()
        money cents currency

    let measurementWindowFromText (value: string) =
        match value with
        | "daily" -> MeasurementWindow.Daily
        | "weekly" -> MeasurementWindow.Weekly
        | "monthly" -> MeasurementWindow.Monthly
        | "quarterly" -> MeasurementWindow.Quarterly
        | "per_incident" -> MeasurementWindow.PerIncident
        | value -> failwith $"Unknown measurement_window '{value}'."

    let accrualStartFromText (value: string) =
        match value with
        | "breach_observed_at" -> AccrualStartFrom.BreachObservedAt
        | "breach_reported_at" -> AccrualStartFrom.BreachReportedAt
        | "next_billing_period_start" -> AccrualStartFrom.NextBillingPeriodStart
        | value -> failwith $"Unknown accrual_start_from '{value}'."

    let contractStatusFromText (value: string) =
        match value with
        | "active" -> ContractStatus.Active
        | "expired" -> ContractStatus.Expired
        | "terminated" -> ContractStatus.Terminated
        | value -> failwith $"Unknown contract status '{value}'."

    let breachStatusFromText (value: string) =
        match value with
        | "pending" -> BreachStatus.Pending
        | "accrued" -> BreachStatus.Accrued
        | "disputed" -> BreachStatus.Disputed
        | "withdrawn" -> BreachStatus.Withdrawn
        | "superseded" -> BreachStatus.Superseded
        | value -> failwith $"Unknown breach status '{value}'."

    let penaltyConfigFromJson (penaltyType: string) (json: string) =
        use document = JsonDocument.Parse json

        let unwrap result =
            match result with
            | Ok value -> value
            | Error error -> failwithf "Invalid persisted penalty config: %A" error

        match penaltyType with
        | "flat_per_breach" -> jsonMoney document "amount_cents" |> PenaltyConfigs.flatPerBreach |> unwrap
        | "percent_of_monthly_fee" ->
            let percent = (requireProperty document "percent").GetDecimal()
            let monthlyFee = jsonMoney document "monthly_fee_cents"
            PenaltyConfigs.percentOfMonthlyFee percent monthlyFee |> unwrap
        | "tiered" ->
            let currency =
                (requireProperty document "currency").GetString()
                |> CurrencyCode.create
                |> unwrap

            let tiers = requireProperty document "tiers"

            let parsedTiers =
                tiers.EnumerateArray()
                |> Seq.map (fun (tier: JsonElement) ->
                    let minBreaches = (tier.GetProperty "min_breaches").GetInt32()

                    let maxBreaches =
                        let mutable value = Unchecked.defaultof<JsonElement>

                        if tier.TryGetProperty("max_breaches", &value) then
                            Some(value.GetInt32())
                        else
                            None

                    let amount =
                        money ((tier.GetProperty "amount_cents").GetInt64()) (CurrencyCode.value currency)

                    PenaltyConfigs.tier minBreaches maxBreaches amount |> unwrap)
                |> List.ofSeq

            PenaltyConfigs.tiered currency parsedTiers |> unwrap
        | "compounding_daily" ->
            let dailyAmount = jsonMoney document "daily_amount_cents"
            let maxDays = (requireProperty document "max_days").GetInt32()
            PenaltyConfigs.compoundingDaily dailyAmount maxDays |> unwrap
        | "linear_per_unit_missed" ->
            let amount = jsonMoney document "amount_per_unit_cents"
            let unitLabel = (requireProperty document "unit_label").GetString()
            PenaltyConfigs.linearPerUnitMissed amount unitLabel |> unwrap
        | value -> failwith $"Unknown penalty_type '{value}'."
