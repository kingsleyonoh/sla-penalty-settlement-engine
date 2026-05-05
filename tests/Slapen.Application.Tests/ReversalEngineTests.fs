namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Application
open Slapen.Data
open Slapen.Domain
open Xunit

[<Collection("postgres")>]
type ReversalEngineTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let globexTenantId = Guid.Parse "20000000-0000-0000-0000-000000000001"
    let acmeCounterpartyId = Guid.Parse "11000000-0000-0000-0000-000000000001"
    let acmeScope = TenantScope.create acmeTenantId
    let globexScope = TenantScope.create globexTenantId

    let execute sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! _ = command.ExecuteNonQueryAsync()
            return ()
        }

    let scalarText sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value :?> string
        }

    let seedAccruedBreach (contractId: Guid) clauseId breachId =
        task {
            let reference = contractId.ToString("N").Substring(24, 8)

            do!
                execute
                    """
                    insert into contracts (
                        id, tenant_id, counterparty_id, reference, title, source, currency,
                        effective_date, expiry_date, status
                    )
                    values (
                        @contract_id, @tenant_id, @counterparty_id, @reference, @title, 'manual',
                        'EUR', '2026-01-01', '2026-12-31', 'active'
                    )
                    """
                    [ "contract_id", contractId :> obj
                      "tenant_id", acmeTenantId :> obj
                      "counterparty_id", acmeCounterpartyId :> obj
                      "reference", $"REV-{reference}" :> obj
                      "title", "Reversal contract" :> obj ]

            do!
                execute
                    """
                    insert into sla_clauses (
                        id, tenant_id, contract_id, reference, metric, measurement_window,
                        target_value, penalty_type, penalty_config, accrual_start_from, active
                    )
                    values (
                        @clause_id, @tenant_id, @contract_id, @reference, 'response_time_minutes',
                        'per_incident', 60.0000, 'flat_per_breach',
                        '{"amount_cents":50000,"currency":"EUR"}'::jsonb,
                        'breach_observed_at', true
                    )
                    """
                    [ "clause_id", clauseId :> obj
                      "tenant_id", acmeTenantId :> obj
                      "contract_id", contractId :> obj
                      "reference", "Schedule reversal" :> obj ]

            do!
                execute
                    """
                    insert into breach_events (
                        id, tenant_id, contract_id, sla_clause_id, source, source_ref,
                        metric_value, observed_at, reported_at, raw_payload, status
                    )
                    values (
                        @breach_id, @tenant_id, @contract_id, @clause_id, 'manual',
                        @source_ref, 92.0000, '2026-05-01T09:00:00Z',
                        '2026-05-01T10:00:00Z', '{}'::jsonb, 'pending'
                    )
                    """
                    [ "breach_id", breachId :> obj
                      "tenant_id", acmeTenantId :> obj
                      "contract_id", contractId :> obj
                      "clause_id", clauseId :> obj
                      "source_ref", $"reversal-{breachId}" :> obj ]

            let! result =
                AccrualWorker.processBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    (DateTimeOffset.Parse "2026-05-05T12:00:00Z")

            match result with
            | AccrualWorker.Accrued _ -> ()
            | other -> failwithf "Expected seed accrual but got %A" other
        }

    [<Fact>]
    member _.``reversal engine writes compensating pairs and marks accrued breach withdrawn``() : Task =
        task {
            let contractId = Guid.Parse "61000000-0000-0000-0000-000000000001"
            let clauseId = Guid.Parse "62000000-0000-0000-0000-000000000001"
            let breachId = Guid.Parse "63000000-0000-0000-0000-000000000001"

            do! seedAccruedBreach contractId clauseId breachId

            let! result =
                ReversalEngine.reverseBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    BreachStatus.Withdrawn
                    ReasonCode.WithdrawnBySource
                    (Some "source retracted breach")
                    CreatedBy.System
                    (DateTimeOffset.Parse "2026-05-06T12:00:00Z")

            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource acmeScope breachId
            let! status = scalarText "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            let reversals =
                rows |> List.filter (fun row -> row.EntryKind = LedgerEntryKind.Reversal)

            match result with
            | ReversalEngine.Reversed ids -> ids |> should haveLength 2
            | other -> failwithf "Expected reversed outcome but got %A" other

            rows |> should haveLength 4
            reversals |> should haveLength 2

            reversals
            |> List.map _.Direction
            |> should equal [ LedgerDirection.CreditOwedToUs; LedgerDirection.Mirror ]

            reversals
            |> List.map (fun row -> Money.cents row.Amount)
            |> should equal [ 50000L; 50000L ]

            reversals
            |> List.forall (fun row -> row.CompensatesLedgerId.IsSome)
            |> should equal true

            status |> should equal "withdrawn"
        }

    [<Fact>]
    member _.``reversal engine rejects invalid transition without ledger changes``() : Task =
        task {
            let contractId = Guid.Parse "61000000-0000-0000-0000-000000000002"
            let clauseId = Guid.Parse "62000000-0000-0000-0000-000000000002"
            let breachId = Guid.Parse "63000000-0000-0000-0000-000000000002"

            do! seedAccruedBreach contractId clauseId breachId

            let! result =
                ReversalEngine.reverseBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    BreachStatus.Pending
                    ReasonCode.OperatorCorrection
                    None
                    CreatedBy.System
                    (DateTimeOffset.Parse "2026-05-06T12:00:00Z")

            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource acmeScope breachId
            let! status = scalarText "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            result
            |> should equal (ReversalEngine.InvalidTransition(BreachStatus.Accrued, BreachStatus.Pending))

            rows |> should haveLength 2
            status |> should equal "accrued"
        }

    [<Fact>]
    member _.``reversal engine hides accrued breaches outside tenant scope``() : Task =
        task {
            let contractId = Guid.Parse "61000000-0000-0000-0000-000000000003"
            let clauseId = Guid.Parse "62000000-0000-0000-0000-000000000003"
            let breachId = Guid.Parse "63000000-0000-0000-0000-000000000003"

            do! seedAccruedBreach contractId clauseId breachId

            let! result =
                ReversalEngine.reverseBreach
                    fixture.DataSource
                    globexScope
                    breachId
                    BreachStatus.Withdrawn
                    ReasonCode.WithdrawnBySource
                    None
                    CreatedBy.System
                    (DateTimeOffset.Parse "2026-05-06T12:00:00Z")

            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource acmeScope breachId
            let! status = scalarText "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            result |> should equal ReversalEngine.NotFound
            rows |> should haveLength 2
            status |> should equal "accrued"
        }
