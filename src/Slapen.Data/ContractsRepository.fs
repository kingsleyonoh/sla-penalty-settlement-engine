namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type ContractRow =
    { Id: Guid
      TenantId: Guid
      CounterpartyId: Guid
      Reference: string
      Title: string
      Currency: string
      Status: string }

[<RequireQualifiedAccess>]
module ContractsRepository =
    let private readContract (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          CounterpartyId = reader.GetGuid(reader.GetOrdinal "counterparty_id")
          Reference = reader.GetString(reader.GetOrdinal "reference")
          Title = reader.GetString(reader.GetOrdinal "title")
          Currency = reader.GetString(reader.GetOrdinal "currency")
          Status = reader.GetString(reader.GetOrdinal "status") }

    let findByReference
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (reference: string)
        : Task<ContractRow option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select id, tenant_id, counterparty_id, reference, title, currency, status
                    from contracts
                    where tenant_id = @tenant_id and reference = @reference
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "reference" reference
            use! reader = command.ExecuteReaderAsync()

            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readContract reader)
            else
                return None
        }
