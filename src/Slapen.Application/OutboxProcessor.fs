namespace Slapen.Application

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data

type OutboxProcessorOptions =
    { BatchSize: int
      MaxAttempts: int
      LeaseSeconds: int
      RetryDelaySeconds: int }

type OutboxProcessResult = { Done: int; Failed: int; Dead: int }

[<RequireQualifiedAccess>]
module OutboxProcessor =
    let private leaseOwner = sprintf "in-process-%s" Environment.MachineName

    let processDue
        (dataSource: NpgsqlDataSource)
        (options: OutboxProcessorOptions)
        (handler: OutboxMessage -> Task<Result<unit, string>>)
        (now: DateTimeOffset)
        : Task<OutboxProcessResult> =
        task {
            let lockedUntil = now.AddSeconds(float options.LeaseSeconds)
            let! leased = OutboxRepository.leaseDue dataSource options.BatchSize leaseOwner now lockedUntil
            let mutable doneCount = 0
            let mutable failedCount = 0
            let mutable deadCount = 0

            for message in leased do
                let! handled = handler message

                match handled with
                | Ok() ->
                    let! marked = OutboxRepository.markDone dataSource message.Id now

                    if marked then
                        doneCount <- doneCount + 1
                | Error error ->
                    let nextRunAt = now.AddSeconds(float options.RetryDelaySeconds)

                    let! status =
                        OutboxRepository.markFailed dataSource message.Id options.MaxAttempts error nextRunAt now

                    if status = "dead" then
                        deadCount <- deadCount + 1
                    else
                        failedCount <- failedCount + 1

            return
                { Done = doneCount
                  Failed = failedCount
                  Dead = deadCount }
        }

[<RequireQualifiedAccess>]
module HangfireOutboxBoundary =
    type RecurringJobRegistration =
        { JobId: string
          Cron: string
          Description: string }

    let registrations pollIntervalSeconds =
        [ { JobId = "slapen-outbox-processor"
            Cron = sprintf "*/%i * * * * *" pollIntervalSeconds
            Description = "Runs the in-process durable outbox processor." } ]
