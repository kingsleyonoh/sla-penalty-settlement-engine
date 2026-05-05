namespace Slapen.Jobs

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Application
open Slapen.Data

[<RequireQualifiedAccess>]
module AccrualJob =
    let execute dataSource scope breachEventId now =
        AccrualWorker.processBreach dataSource scope breachEventId now

    let executePending (runner: DateTimeOffset -> Task<int>) (now: DateTimeOffset) : Task<int> = runner now
