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
type AccrualWorkerTests(fixture: PostgresFixture) =
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

    let insertContract (contractId: Guid) reference =
        execute
            """
            insert into contracts (
                id, tenant_id, counterparty_id, reference, title, source, currency,
                effective_date, expiry_date, status
            )
            values (
                @contract_id, @tenant_id, @counterparty_id, @reference, @title, 'manual', 'EUR',
                '2026-01-01', '2026-12-31', 'active'
            )
            """
            [ "contract_id", contractId :> obj
              "tenant_id", acmeTenantId :> obj
              "counterparty_id", acmeCounterpartyId :> obj
              "reference", reference :> obj
              "title", $"Accrual contract {reference}" :> obj ]

    let insertFlatClause (clauseId: Guid) contractId active amountCents =
        let clauseReference = clauseId.ToString("N").Substring(0, 8)

        execute
            """
            insert into sla_clauses (
                id, tenant_id, contract_id, reference, metric, measurement_window, target_value,
                penalty_type, penalty_config, accrual_start_from, active
            )
            values (
                @clause_id, @tenant_id, @contract_id, @reference, 'response_time_minutes',
                'per_incident', 60.0000, 'flat_per_breach', @config::jsonb,
                'breach_observed_at', @active
            )
            """
            [ "clause_id", clauseId :> obj
              "tenant_id", acmeTenantId :> obj
              "contract_id", contractId :> obj
              "reference", $"Schedule {clauseReference}" :> obj
              "config", $"""{{"amount_cents":{amountCents},"currency":"EUR"}}""" :> obj
              "active", active :> obj ]

    let insertBreach breachId contractId clauseId status (observedAt: DateTimeOffset) =
        execute
            """
            insert into breach_events (
                id, tenant_id, contract_id, sla_clause_id, source, source_ref, metric_value,
                observed_at, reported_at, raw_payload, status
            )
            values (
                @breach_id, @tenant_id, @contract_id, @clause_id, 'manual', @source_ref,
                92.0000, @observed_at, @reported_at, '{}'::jsonb, @status
            )
            """
            [ "breach_id", breachId :> obj
              "tenant_id", acmeTenantId :> obj
              "contract_id", contractId :> obj
              "clause_id", clauseId :> obj
              "source_ref", $"test-{breachId}" :> obj
              "observed_at", observedAt :> obj
              "reported_at", observedAt.AddHours(1.0) :> obj
              "status", status :> obj ]

    let insertFlatScenario (contractId: Guid) clauseId breachId active status amountCents =
        task {
            let reference = contractId.ToString("N").Substring(24, 8)
            do! insertContract contractId $"ACC-{reference}"
            do! insertFlatClause clauseId contractId active amountCents
            do! insertBreach breachId contractId clauseId status (DateTimeOffset.Parse "2026-05-01T09:00:00Z")
        }

    [<Fact>]
    member _.``accrual worker posts penalty pair and marks pending breach accrued``() : Task =
        task {
            let contractId = Guid.Parse "51000000-0000-0000-0000-000000000001"
            let clauseId = Guid.Parse "52000000-0000-0000-0000-000000000001"
            let breachId = Guid.Parse "53000000-0000-0000-0000-000000000001"

            do! insertFlatScenario contractId clauseId breachId true "pending" 50000L

            let! result =
                AccrualWorker.processBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    (DateTimeOffset.Parse "2026-05-05T12:00:00Z")

            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource acmeScope breachId
            let! status = scalarText "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            match result with
            | AccrualWorker.Accrued ids -> ids |> should haveLength 2
            | other -> failwithf "Expected accrual outcome but got %A" other

            rows |> should haveLength 2

            rows
            |> List.map _.EntryKind
            |> should equal [ LedgerEntryKind.Accrual; LedgerEntryKind.Accrual ]

            rows
            |> List.map (fun row -> Money.cents row.Amount)
            |> should equal [ 50000L; 50000L ]

            status |> should equal "accrued"
        }

    [<Fact>]
    member _.``accrual worker leaves breach pending when rules produce no penalty``() : Task =
        task {
            let contractId = Guid.Parse "51000000-0000-0000-0000-000000000002"
            let clauseId = Guid.Parse "52000000-0000-0000-0000-000000000002"
            let breachId = Guid.Parse "53000000-0000-0000-0000-000000000002"

            do! insertFlatScenario contractId clauseId breachId false "pending" 50000L

            let! result =
                AccrualWorker.processBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    (DateTimeOffset.Parse "2026-05-05T12:00:00Z")

            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource acmeScope breachId
            let! status = scalarText "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            match result with
            | AccrualWorker.NoPenalty ClauseInactive -> ()
            | other -> failwithf "Expected inactive clause no-penalty outcome but got %A" other

            rows |> should haveLength 0
            status |> should equal "pending"
        }

    [<Fact>]
    member _.``accrual worker hides breaches outside tenant scope``() : Task =
        task {
            let contractId = Guid.Parse "51000000-0000-0000-0000-000000000003"
            let clauseId = Guid.Parse "52000000-0000-0000-0000-000000000003"
            let breachId = Guid.Parse "53000000-0000-0000-0000-000000000003"

            do! insertFlatScenario contractId clauseId breachId true "pending" 50000L

            let! result =
                AccrualWorker.processBreach
                    fixture.DataSource
                    globexScope
                    breachId
                    (DateTimeOffset.Parse "2026-05-05T12:00:00Z")

            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource acmeScope breachId
            let! status = scalarText "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            result |> should equal AccrualWorker.NotFound
            rows |> should haveLength 0
            status |> should equal "pending"
        }
