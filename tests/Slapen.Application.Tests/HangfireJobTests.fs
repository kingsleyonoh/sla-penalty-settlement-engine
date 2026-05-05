namespace Slapen.Application.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Slapen.Jobs
open Xunit

type HangfireJobTests() =
    [<Fact>]
    member _.``registration metadata covers every phase two recurring job``() =
        let registrations = HangfireWiring.registrations 10
        let ids = registrations |> List.map _.JobId

        ids
        |> should
            equal
            [ "slapen-accrual"
              "slapen-settlement-builder"
              "slapen-outbox-processor"
              "slapen-stale-breach-reminder"
              "slapen-stale-ingestion-detector"
              "slapen-outbox-dead-letter-reaper" ]

    [<Fact>]
    member _.``thin job wrappers expose testable execute methods``() : Task =
        task {
            let now = DateTimeOffset.Parse "2026-05-05T10:00:00Z"
            let! stale = StaleIngestionDetectorJob.execute (fun _ -> Task.FromResult 3) now
            let! reaped = OutboxDeadLetterReaperJob.execute (fun _ -> Task.FromResult 2) now

            stale |> should equal 3
            reaped |> should equal 2
        }
