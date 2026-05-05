namespace Slapen.Data

open System
open System.Threading.Tasks
open Npgsql

type AdapterReadiness =
    { Adapter: string
      Healthy: bool
      Detail: string }

[<RequireQualifiedAccess>]
module ReadinessRepository =
    let db (dataSource: NpgsqlDataSource) : Task<bool> =
        task {
            try
                use! connection = dataSource.OpenConnectionAsync().AsTask()

                use command =
                    new NpgsqlCommand("select coalesce(max(version), 0) from schema_migrations", connection)

                let! value = command.ExecuteScalarAsync()
                return (value :?> int) > 0
            with _ ->
                return false
        }

    let outbox (dataSource: NpgsqlDataSource) : Task<bool> =
        task {
            try
                use! connection = dataSource.OpenConnectionAsync().AsTask()

                use command =
                    new NpgsqlCommand("select count(*)::bigint from outbox where status = 'dead'", connection)

                let! value = command.ExecuteScalarAsync()
                return (value :?> int64) = 0L
            with _ ->
                return false
        }

    let adapters
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (now: DateTimeOffset)
        : Task<AdapterReadiness list> =
        task {
            let! settings = IngestionSettingsRepository.list dataSource scope
            let checks = ResizeArray<AdapterReadiness>()

            for setting in settings do
                if setting.Enabled then
                    let healthy =
                        match setting.LastTestedAt, setting.LastTestStatus with
                        | Some testedAt, Some "healthy" ->
                            testedAt >= now.AddSeconds(float (-2 * setting.PollIntervalSeconds))
                        | _ -> false

                    checks.Add(
                        { Adapter = setting.Adapter
                          Healthy = healthy
                          Detail =
                            if healthy then
                                "healthy"
                            else
                                "enabled adapter has no recent healthy test" }
                    )

            return List.ofSeq checks
        }
