namespace Slapen.Jobs

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Application
open Slapen.Data

[<RequireQualifiedAccess>]
module OutboxWorkerJob =
    let execute dataSource options handler now =
        OutboxProcessor.processDue dataSource options handler now
