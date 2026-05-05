namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type SettlementListItem =
    { Id: Guid
      CounterpartyName: string
      ContractReference: string
      AmountCents: int64
      Currency: string
      Status: string
      CreatedAt: DateTimeOffset }

[<RequireQualifiedAccess>]
module SettlementUiRepository =
    let private readItem (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          CounterpartyName = reader.GetString(reader.GetOrdinal "counterparty_name")
          ContractReference = reader.GetString(reader.GetOrdinal "contract_reference")
          AmountCents = reader.GetInt64(reader.GetOrdinal "amount_cents")
          Currency = reader.GetString(reader.GetOrdinal "currency")
          Status = reader.GetString(reader.GetOrdinal "status")
          CreatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "created_at") }

    let list (dataSource: NpgsqlDataSource) (scope: TenantScope) limit : Task<SettlementListItem list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select s.id, cp.canonical_name as counterparty_name,
                           c.reference as contract_reference, s.amount_cents,
                           s.currency, s.status, s.created_at
                    from settlements s
                    join counterparties cp on cp.tenant_id = s.tenant_id and cp.id = s.counterparty_id
                    join contracts c on c.tenant_id = s.tenant_id and c.id = s.contract_id
                    where s.tenant_id = @tenant_id
                    order by s.created_at desc
                    limit @limit
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "limit" limit
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<SettlementListItem>()

            while reader.Read() do
                rows.Add(readItem reader)

            return List.ofSeq rows
        }

    let approve (dataSource: NpgsqlDataSource) (scope: TenantScope) settlementId approvedAt =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update settlements
                    set status = 'ready',
                        approved_at = coalesce(approved_at, @approved_at)
                    where tenant_id = @tenant_id
                      and id = @id
                      and status in ('awaiting_approval', 'ready')
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "id" settlementId
            Sql.addParameter command "approved_at" approvedAt
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }
