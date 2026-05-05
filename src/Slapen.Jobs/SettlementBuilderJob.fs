namespace Slapen.Jobs

open System
open Npgsql
open Slapen.Application
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module SettlementBuilderJob =
    let execute dataSource scope periodStart periodEnd now =
        SettlementBuilder.buildPending dataSource scope periodStart periodEnd CreatedBy.System now
