namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type DashboardBreach =
    { Id: Guid
      Status: string
      ObservedAt: DateTimeOffset }

type DashboardSummary =
    { PendingBreaches: int64
      ActiveContracts: int64
      LedgerMovementCents: int64
      RecentPending: DashboardBreach list
      QueryCount: int }

[<RequireQualifiedAccess>]
module DashboardRepository =
    let private readBreach (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          Status = reader.GetString(reader.GetOrdinal "status")
          ObservedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "observed_at") }

    let summary (dataSource: NpgsqlDataSource) (scope: TenantScope) (limit: int) : Task<DashboardSummary> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select
                      (select count(*)::bigint from breach_events where tenant_id = @tenant_id and status = 'pending') as pending_breaches,
                      (select count(*)::bigint from contracts where tenant_id = @tenant_id and status = 'active') as active_contracts,
                      (select coalesce(sum(amount_cents), 0)::bigint from penalty_ledger where tenant_id = @tenant_id) as ledger_movement_cents;

                    select id, status, observed_at
                    from breach_events
                    where tenant_id = @tenant_id and status = 'pending'
                    order by observed_at desc
                    limit @limit;
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "limit" limit
            use! reader = command.ExecuteReaderAsync()
            let! hasStats = reader.ReadAsync()

            if not hasStats then
                return
                    { PendingBreaches = 0L
                      ActiveContracts = 0L
                      LedgerMovementCents = 0L
                      RecentPending = []
                      QueryCount = 1 }
            else
                let pending = reader.GetInt64(reader.GetOrdinal "pending_breaches")
                let contracts = reader.GetInt64(reader.GetOrdinal "active_contracts")
                let movement = reader.GetInt64(reader.GetOrdinal "ledger_movement_cents")
                let! _ = reader.NextResultAsync()
                let rows = ResizeArray<DashboardBreach>()

                while reader.Read() do
                    rows.Add(readBreach reader)

                return
                    { PendingBreaches = pending
                      ActiveContracts = contracts
                      LedgerMovementCents = movement
                      RecentPending = List.ofSeq rows
                      QueryCount = 1 }
        }
