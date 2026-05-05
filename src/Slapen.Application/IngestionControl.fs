namespace Slapen.Application

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data

[<RequireQualifiedAccess>]
module IngestionControl =
    type TestOutcome =
        | AdapterDisabled
        | AdapterHealthy
        | AdapterFailed of string

    type PullOutcome =
        | PullSkippedDisabled
        | PullRequested

    let private validAdapter adapter =
        IngestionSettingsRepository.adapters
        |> List.exists (fun (known, _) -> known = adapter)

    let testAdapter (dataSource: NpgsqlDataSource) (scope: TenantScope) adapter now : Task<TestOutcome> =
        task {
            if not (validAdapter adapter) then
                return AdapterFailed "unknown_adapter"
            else
                let! setting = IngestionSettingsRepository.find dataSource scope adapter

                match setting |> Option.map _.Enabled |> Option.defaultValue false with
                | false ->
                    let! _ = IngestionSettingsRepository.recordTest dataSource scope adapter "disabled" None now
                    return AdapterDisabled
                | true ->
                    let! _ = IngestionSettingsRepository.recordTest dataSource scope adapter "healthy" None now
                    return AdapterHealthy
        }

    let requestPullNow (dataSource: NpgsqlDataSource) (scope: TenantScope) adapter now : Task<PullOutcome> =
        task {
            let! setting = IngestionSettingsRepository.find dataSource scope adapter

            match setting |> Option.map _.Enabled |> Option.defaultValue false with
            | false -> return PullSkippedDisabled
            | true ->
                let! _ = IngestionSettingsRepository.requestPull dataSource scope adapter now
                return PullRequested
        }
