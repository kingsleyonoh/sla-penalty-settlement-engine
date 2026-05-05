namespace Slapen.Application

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module LedgerWriter =
    let writePairWithinTransaction
        (connection: NpgsqlConnection)
        (transaction: NpgsqlTransaction)
        (scope: TenantScope)
        (credit: LedgerEntryCandidate)
        (mirror: LedgerEntryCandidate)
        : Task<Result<Guid list, DomainError>> =
        task {
            match LedgerPair.create credit mirror with
            | Error error -> return Error error
            | Ok pair ->
                let! ids = PenaltyLedgerRepository.insertPair connection transaction scope pair
                return Ok ids
        }

    let writePair
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (credit: LedgerEntryCandidate)
        (mirror: LedgerEntryCandidate)
        : Task<Result<Guid list, DomainError>> =
        task {
            match LedgerPair.create credit mirror with
            | Error error -> return Error error
            | Ok pair ->
                use! connection = dataSource.OpenConnectionAsync().AsTask()
                use! transaction = connection.BeginTransactionAsync()

                try
                    let! ids = PenaltyLedgerRepository.insertPair connection transaction scope pair
                    do! transaction.CommitAsync()
                    return Ok ids
                with error ->
                    do! transaction.RollbackAsync()
                    return raise error
        }
