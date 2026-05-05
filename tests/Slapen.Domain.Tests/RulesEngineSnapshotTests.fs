namespace Slapen.Domain.Tests

open System
open System.Threading.Tasks
open Slapen.Domain
open VerifyTests
open VerifyXunit
open Xunit

type SnapshotCase = { Case: string; Result: string }

type RulesEngineSnapshotTests() =
    let tenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let contractId = Guid.Parse "12000000-0000-0000-0000-000000000001"
    let counterpartyId = Guid.Parse "11000000-0000-0000-0000-000000000001"
    let clauseId = Guid.Parse "13000000-0000-0000-0000-000000000001"
    let breachId = Guid.Parse "14000000-0000-0000-0000-000000000001"
    let asOf = DateTimeOffset.Parse "2026-05-07T00:00:00Z"
    let observedAt = DateTimeOffset.Parse "2026-05-01T00:00:00Z"
    let reportedAt = DateTimeOffset.Parse "2026-05-01T08:00:00Z"

    let ok result =
        match result with
        | Ok value -> value
        | Error error -> failwithf "Expected Ok but got %A" error

    let contract currency effectiveDate =
        { Id = contractId
          TenantId = tenantId
          CounterpartyId = counterpartyId
          Reference = "MSA-001"
          Currency = CurrencyCode.create currency |> ok
          EffectiveDate = DateOnly.Parse effectiveDate
          ExpiryDate = None
          Status = ContractStatus.Active }

    let clause penaltyConfig capPerPeriod capPerContract active startFrom =
        { Id = clauseId
          TenantId = tenantId
          ContractId = contractId
          Reference = "Schedule B 2.3.1"
          Metric = "response_time_minutes"
          MeasurementWindow = MeasurementWindow.Monthly
          TargetValue = 60.0M
          PenaltyConfig = penaltyConfig
          CapPerPeriod = capPerPeriod
          CapPerContract = capPerContract
          AccrualStartFrom = startFrom
          Active = active }

    let breach unitsMissed observed =
        { Id = breachId
          TenantId = tenantId
          ContractId = contractId
          SlaClauseId = clauseId
          MetricValue = 92.0M
          UnitsMissed = unitsMissed
          ObservedAt = observed
          ReportedAt = reportedAt
          ResolvedAt = None
          Status = BreachStatus.Pending }

    let previousCurrency id cents currency =
        LedgerFactory.candidate LedgerDirection.CreditOwedToUs (TestValues.money cents currency)
        |> fun entry ->
            { entry with
                Id = id
                BreachEventId = Guid.NewGuid() }

    let previous id cents = previousCurrency id cents "EUR"

    let input contract clause breach previousAccruals priorBreachCount asOf =
        { Contract = contract
          Clause = clause
          Breach = breach
          PreviousAccruals = previousAccruals
          PriorMeasurementWindowBreachCount = priorBreachCount
          AsOf = asOf }

    let renderMoney amount =
        sprintf "%d %s" (Money.cents amount) (Money.currency amount)

    let render result =
        match result with
        | Ok(Accrued amount) -> sprintf "Accrued %s" (renderMoney amount)
        | Ok(CapApplied(amount, detail)) ->
            sprintf
                "CapApplied %s kind=%A cap=%s prior=%s uncapped=%s"
                (renderMoney amount)
                detail.Kind
                (renderMoney detail.Cap)
                (renderMoney detail.PriorAccrued)
                (renderMoney detail.Uncapped)
        | Ok(NoPenalty reason) -> sprintf "NoPenalty %A" reason
        | Error error -> sprintf "Error %A" error

    let calculate name input =
        { Case = name
          Result = RulesEngine.calculatePenalty input |> render }

    [<Fact>]
    member _.PenaltyRulesAreSnapshotPinned() : Task =
        let eurContract = contract "EUR" "2026-01-01"
        let flat = PenaltyConfigs.flatPerBreach (TestValues.money 50000L "EUR") |> ok

        let percent =
            PenaltyConfigs.percentOfMonthlyFee 5.0M (TestValues.money 1000000L "EUR") |> ok

        let firstTier = PenaltyConfigs.tier 1 (Some 3) (TestValues.money 50000L "EUR") |> ok
        let secondTier = PenaltyConfigs.tier 4 None (TestValues.money 150000L "EUR") |> ok

        let tiered =
            PenaltyConfigs.tiered (CurrencyCode.create "EUR" |> ok) [ firstTier; secondTier ]
            |> ok

        let compounding =
            PenaltyConfigs.compoundingDaily (TestValues.money 10000L "EUR") 30 |> ok

        let linear =
            PenaltyConfigs.linearPerUnitMissed (TestValues.money 7500L "EUR") "ticket" |> ok

        let priorCapEntry =
            previous (Guid.Parse "15000000-0000-0000-0000-000000000001") 300000L

        let priorTierEntry =
            previous (Guid.Parse "15000000-0000-0000-0000-000000000002") 50000L

        let cases =
            [ calculate
                  "flat_per_breach happy path"
                  (input
                      eurContract
                      (clause flat None None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      []
                      0
                      asOf)
              calculate
                  "flat_per_breach period cap"
                  (input
                      eurContract
                      (clause flat (Some(TestValues.money 40000L "EUR")) None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      []
                      0
                      asOf)
              calculate
                  "percent_of_monthly_fee prorates days in breach"
                  (input
                      eurContract
                      (clause percent None None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      []
                      0
                      asOf)
              calculate
                  "tiered accrues differential when crossing tier"
                  (input
                      eurContract
                      (clause tiered None None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      [ priorTierEntry ]
                      3
                      asOf)
              calculate
                  "tiered overflow uses last tier already reached"
                  (input
                      eurContract
                      (clause tiered None None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      [ priorTierEntry ]
                      99
                      asOf)
              calculate
                  "prior accrual currency mismatch is rejected"
                  (input
                      eurContract
                      (clause tiered None None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      [ previousCurrency (Guid.Parse "15000000-0000-0000-0000-000000000003") 50000L "USD" ]
                      3
                      asOf)
              calculate
                  "compounding_daily max days cap"
                  (input
                      eurContract
                      (clause compounding None None true AccrualStartFrom.BreachObservedAt)
                      (breach None (DateTimeOffset.Parse "2026-01-01T00:00:00Z"))
                      []
                      0
                      (DateTimeOffset.Parse "2026-02-15T00:00:00Z"))
              calculate
                  "compounding_daily max already reached"
                  (input
                      eurContract
                      (clause compounding None None true AccrualStartFrom.BreachObservedAt)
                      (breach None (DateTimeOffset.Parse "2026-01-01T00:00:00Z"))
                      [ priorCapEntry ]
                      0
                      (DateTimeOffset.Parse "2026-02-20T00:00:00Z"))
              calculate
                  "linear_per_unit_missed happy path"
                  (input
                      eurContract
                      (clause linear None None true AccrualStartFrom.BreachObservedAt)
                      (breach (Some 4.0M) observedAt)
                      []
                      0
                      asOf)
              calculate
                  "linear_per_unit_missed missing units"
                  (input
                      eurContract
                      (clause linear None None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      []
                      0
                      asOf)
              calculate
                  "inactive clause has no penalty"
                  (input
                      eurContract
                      (clause flat None None false AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      []
                      0
                      asOf)
              calculate
                  "breach before contract effective date is rejected"
                  (input
                      (contract "EUR" "2026-06-01")
                      (clause flat None None true AccrualStartFrom.BreachObservedAt)
                      (breach None observedAt)
                      []
                      0
                      asOf) ]

        let settings = VerifySettings()

        let verifier =
            new InnerVerifier(__SOURCE_DIRECTORY__, "RulesEngineSnapshotTests.PenaltyRulesAreSnapshotPinned", settings)

        verifier.Verify(cases) :> Task

    [<Fact>]
    member _.``linear penalty returns a domain error when units are missing``() =
        let linear =
            PenaltyConfigs.linearPerUnitMissed (TestValues.money 7500L "EUR") "ticket" |> ok

        let calculation =
            input
                (contract "EUR" "2026-01-01")
                (clause linear None None true AccrualStartFrom.BreachObservedAt)
                (breach None observedAt)
                []
                0
                asOf

        let result = RulesEngine.calculatePenalty calculation

        Assert.Equal<Result<PenaltyResult, DomainError>>(Error(MissingRequiredMetric "units_missed"), result)
