namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Application
open Slapen.Data
open Slapen.Domain
open Xunit

[<CollectionDefinition("postgres")>]
type PostgresCollection() =
    interface ICollectionFixture<PostgresFixture>

[<Collection("postgres")>]
type LedgerWriterTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let globexTenantId = Guid.Parse "20000000-0000-0000-0000-000000000001"
    let acmeContractId = Guid.Parse "12000000-0000-0000-0000-000000000001"
    let acmeCounterpartyId = Guid.Parse "11000000-0000-0000-0000-000000000001"
    let acmeClauseId = Guid.Parse "13000000-0000-0000-0000-000000000001"
    let acmeBreachId = Guid.Parse "14000000-0000-0000-0000-000000000001"
    let periodStart = DateTimeOffset.Parse "2026-05-01T00:00:00Z"
    let periodEnd = DateTimeOffset.Parse "2026-05-31T23:59:59Z"
    let createdAt = DateTimeOffset.Parse "2026-05-05T12:00:00Z"

    let money cents =
        match Money.create cents "EUR" with
        | Ok amount -> amount
        | Error error -> failwithf "Unexpected money error %A" error

    let candidate id direction amount =
        { Id = id
          TenantId = acmeTenantId
          ContractId = acmeContractId
          CounterpartyId = acmeCounterpartyId
          SlaClauseId = acmeClauseId
          BreachEventId = acmeBreachId
          EntryKind = LedgerEntryKind.Accrual
          Direction = direction
          Amount = amount
          AccrualPeriodStart = periodStart
          AccrualPeriodEnd = periodEnd
          CompensatesLedgerId = None
          ReasonCode = ReasonCode.SlaBreach
          ReasonNotes = Some "fixture accrual"
          CreatedAt = createdAt
          CreatedBy = CreatedBy.System }

    let scalarInt64 sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! value = command.ExecuteScalarAsync()
            return value :?> int64
        }

    let execute sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! _ = command.ExecuteNonQueryAsync()
            return ()
        }

    [<Fact>]
    member _.``ledger writer inserts bilateral pair atomically through domain invariant``() : Task =
        task {
            let scope = TenantScope.create acmeTenantId
            let amount = money 50000L

            let credit =
                candidate (Guid.Parse "91000000-0000-0000-0000-000000000001") LedgerDirection.CreditOwedToUs amount

            let mirror =
                candidate (Guid.Parse "91000000-0000-0000-0000-000000000002") LedgerDirection.Mirror amount

            let! result = LedgerWriter.writePair fixture.DataSource scope credit mirror
            let! rows = PenaltyLedgerRepository.listByBreach fixture.DataSource scope acmeBreachId
            let globexScope = TenantScope.create globexTenantId
            let! hiddenRows = PenaltyLedgerRepository.listByBreach fixture.DataSource globexScope acmeBreachId

            match result with
            | Ok ids -> ids |> should haveLength 2
            | Error error -> failwithf "Expected ledger pair to write but got %A" error

            rows |> should haveLength 2
            rows |> List.map _.Direction |> should contain LedgerDirection.CreditOwedToUs
            rows |> List.map _.Direction |> should contain LedgerDirection.Mirror

            rows
            |> List.map (fun row -> Money.cents row.Amount)
            |> should equal [ 50000L; 50000L ]

            hiddenRows |> should haveLength 0
        }

    [<Fact>]
    member _.``ledger writer rejects invalid pair before any insert``() : Task =
        task {
            let scope = TenantScope.create acmeTenantId

            let credit =
                candidate
                    (Guid.Parse "92000000-0000-0000-0000-000000000001")
                    LedgerDirection.CreditOwedToUs
                    (money 50000L)

            let mirror =
                candidate (Guid.Parse "92000000-0000-0000-0000-000000000002") LedgerDirection.Mirror (money 49999L)

            let! result = LedgerWriter.writePair fixture.DataSource scope credit mirror

            let! count =
                scalarInt64
                    "select count(*) from penalty_ledger where id in (@credit_id, @mirror_id)"
                    [ "credit_id", credit.Id :> obj; "mirror_id", mirror.Id :> obj ]

            match result with
            | Error(LedgerPairMismatch "amount must match") -> ()
            | other -> failwithf "Expected amount mismatch error but got %A" other

            count |> should equal 0L
        }

    [<Fact>]
    member _.``penalty ledger remains append only after application writes``() : Task =
        task {
            let scope = TenantScope.create acmeTenantId
            let amount = money 60000L

            let credit =
                candidate (Guid.Parse "93000000-0000-0000-0000-000000000001") LedgerDirection.CreditOwedToUs amount

            let mirror =
                candidate (Guid.Parse "93000000-0000-0000-0000-000000000002") LedgerDirection.Mirror amount

            let! _ = LedgerWriter.writePair fixture.DataSource scope credit mirror

            let! updateBlocked =
                task {
                    try
                        do!
                            execute
                                "update penalty_ledger set amount_cents = 1 where id = @id"
                                [ "id", credit.Id :> obj ]

                        return false
                    with :? PostgresException as error ->
                        return error.SqlState = "P0001"
                }

            updateBlocked |> should equal true
        }
