namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type IngestionAdapterSetting =
    { TenantId: Guid
      Adapter: string
      DisplayName: string
      Enabled: bool
      PollIntervalSeconds: int
      LastTestedAt: DateTimeOffset option
      LastTestStatus: string option
      LastTestError: string option
      LastPullRequestedAt: DateTimeOffset option }

[<RequireQualifiedAccess>]
module IngestionSettingsRepository =
    let adapters =
        [ "manual", "Manual"
          "csv_import", "CSV import"
          "contract_lifecycle_rest", "Contract Lifecycle REST"
          "contract_lifecycle_nats", "Contract Lifecycle NATS"
          "hub_ingress", "Hub ingress" ]

    let private dateOption (reader: DbDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetFieldValue<DateTimeOffset> ordinal)

    let private readSetting (reader: DbDataReader) =
        { TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          Adapter = reader.GetString(reader.GetOrdinal "adapter")
          DisplayName = reader.GetString(reader.GetOrdinal "display_name")
          Enabled = reader.GetBoolean(reader.GetOrdinal "enabled")
          PollIntervalSeconds = reader.GetInt32(reader.GetOrdinal "poll_interval_seconds")
          LastTestedAt = dateOption reader "last_tested_at"
          LastTestStatus = Sql.stringOption reader "last_test_status"
          LastTestError = Sql.stringOption reader "last_test_error"
          LastPullRequestedAt = dateOption reader "last_pull_requested_at" }

    let private valuesSql =
        adapters
        |> List.map (fun (adapter, displayName) -> $"('{adapter}', '{displayName}')")
        |> String.concat ","

    let list (dataSource: NpgsqlDataSource) (scope: TenantScope) : Task<IngestionAdapterSetting list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    $"""
                    with known(adapter, display_name) as (values {valuesSql})
                    select
                        @tenant_id::uuid as tenant_id,
                        known.adapter,
                        known.display_name,
                        coalesce(settings.enabled, false) as enabled,
                        coalesce(settings.poll_interval_seconds, 900) as poll_interval_seconds,
                        settings.last_tested_at,
                        settings.last_test_status,
                        settings.last_test_error,
                        settings.last_pull_requested_at
                    from known
                    left join ingestion_adapter_settings settings
                      on settings.tenant_id = @tenant_id
                     and settings.adapter = known.adapter
                    order by known.adapter
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<IngestionAdapterSetting>()

            while reader.Read() do
                rows.Add(readSetting reader)

            return List.ofSeq rows
        }

    let find (dataSource: NpgsqlDataSource) (scope: TenantScope) adapter =
        task {
            let! settings = list dataSource scope
            return settings |> List.tryFind (fun item -> item.Adapter = adapter)
        }

    let private upsert
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        adapter
        enabled
        testStatus
        testError
        testedAt
        pullAt
        now
        =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into ingestion_adapter_settings (
                        tenant_id, adapter, enabled, last_test_status, last_test_error,
                        last_tested_at, last_pull_requested_at, updated_at
                    )
                    values (
                        @tenant_id, @adapter, @enabled, @test_status, @test_error,
                        @tested_at, @pull_at, @now
                    )
                    on conflict (tenant_id, adapter) do update
                    set enabled = excluded.enabled,
                        last_test_status = coalesce(excluded.last_test_status, ingestion_adapter_settings.last_test_status),
                        last_test_error = excluded.last_test_error,
                        last_tested_at = coalesce(excluded.last_tested_at, ingestion_adapter_settings.last_tested_at),
                        last_pull_requested_at = coalesce(excluded.last_pull_requested_at, ingestion_adapter_settings.last_pull_requested_at),
                        updated_at = excluded.updated_at
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "adapter" adapter
            Sql.addParameter command "enabled" enabled
            Sql.addOptionalParameter command "test_status" (testStatus |> Option.map box)
            Sql.addOptionalParameter command "test_error" (testError |> Option.map box)
            Sql.addOptionalParameter command "tested_at" (testedAt |> Option.map box)
            Sql.addOptionalParameter command "pull_at" (pullAt |> Option.map box)
            Sql.addParameter command "now" now
            let! _ = command.ExecuteNonQueryAsync()
            return! find dataSource scope adapter
        }

    let setEnabled (dataSource: NpgsqlDataSource) (scope: TenantScope) adapter enabled now =
        task {
            let! setting = upsert dataSource scope adapter enabled None None None None now
            return setting.Value
        }

    let recordTest (dataSource: NpgsqlDataSource) (scope: TenantScope) adapter status error now =
        task {
            let! current = find dataSource scope adapter
            let enabled = current |> Option.map _.Enabled |> Option.defaultValue false
            let! setting = upsert dataSource scope adapter enabled (Some status) error (Some now) None now
            return setting.Value
        }

    let requestPull (dataSource: NpgsqlDataSource) (scope: TenantScope) adapter now =
        task {
            let! current = find dataSource scope adapter
            let enabled = current |> Option.map _.Enabled |> Option.defaultValue false
            let! setting = upsert dataSource scope adapter enabled None None None (Some now) now
            return setting.Value
        }
