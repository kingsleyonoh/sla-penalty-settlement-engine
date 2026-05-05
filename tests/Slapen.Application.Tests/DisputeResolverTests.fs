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
type DisputeResolverTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let globexTenantId = Guid.Parse "20000000-0000-0000-0000-000000000001"
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

    let scalarString sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value :?> string
        }

    let seedAccrual breachId sourceRef =
        task {
            do!
                execute
                    """
                    insert into breach_events (
                        id, tenant_id, contract_id, sla_clause_id, source, source_ref,
                        metric_value, observed_at, reported_at, raw_payload, status
                    )
                    values (
                        @breach_id, @tenant_id, '12000000-0000-0000-0000-000000000001',
                        '13000000-0000-0000-0000-000000000001', 'manual', @source_ref,
                        93.0000, '2026-05-07T09:00:00Z', '2026-05-07T09:10:00Z',
                        '{}'::jsonb, 'pending'
                    )
                    on conflict (tenant_id, source, source_ref) where source_ref is not null do nothing
                    """
                    [ "breach_id", breachId :> obj
                      "tenant_id", acmeTenantId :> obj
                      "source_ref", sourceRef :> obj ]

            let! result =
                AccrualWorker.processBreach
                    fixture.DataSource
                    acmeScope
                    breachId
                    (DateTimeOffset.Parse "2026-05-07T11:00:00Z")

            match result with
            | AccrualWorker.Accrued _ -> ()
            | other -> failwithf "Expected accrual, got %A" other
        }

    [<Fact>]
    member _.``dispute resolver reverses accrued ledger when dispute is resolved against us``() : Task =
        task {
            let breachId = Guid.Parse "66000000-0000-0000-0000-000000000001"
            do! seedAccrual breachId "dispute-against-001"

            let! opened =
                DisputeResolver.openDispute
                    fixture.DataSource
                    acmeScope
                    breachId
                    "Supplier provided valid exception evidence"
                    None
                    (DateTimeOffset.Parse "2026-05-08T09:00:00Z")

            let! resolved =
                DisputeResolver.resolveDispute
                    fixture.DataSource
                    acmeScope
                    breachId
                    DisputeResolver.ResolvedAgainstUs
                    "credit withdrawn after evidence review"
                    CreatedBy.System
                    (DateTimeOffset.Parse "2026-05-09T09:00:00Z")

            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource acmeScope breachId
            let! status = scalarString "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            let reversals =
                rows |> List.filter (fun row -> row.EntryKind = LedgerEntryKind.Reversal)

            opened |> should equal DisputeResolver.DisputeOpened

            resolved
            |> should equal (DisputeResolver.DisputeResolved(ReversalIds = (reversals |> List.map _.Id)))

            status |> should equal "withdrawn"
            rows |> should haveLength 4
            reversals |> should haveLength 2

            reversals
            |> List.forall (fun row -> row.CompensatesLedgerId.IsSome)
            |> should equal true
        }

    [<Fact>]
    member _.``dispute resolver hides breaches outside tenant scope``() : Task =
        task {
            let breachId = Guid.Parse "66000000-0000-0000-0000-000000000002"
            do! seedAccrual breachId "dispute-tenant-001"

            let! result =
                DisputeResolver.openDispute
                    fixture.DataSource
                    globexScope
                    breachId
                    "wrong tenant attempt"
                    None
                    (DateTimeOffset.Parse "2026-05-08T09:00:00Z")

            let! status = scalarString "select status from breach_events where id = @id" [ "id", breachId :> obj ]

            result |> should equal DisputeResolver.NotFound
            status |> should equal "accrued"
        }
