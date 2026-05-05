namespace Slapen.Jobs

open System
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module StaleBreachReminderJob =
    let execute (runner: DateTimeOffset -> Task<int>) (now: DateTimeOffset) : Task<int> = runner now
