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

type NewContract =
    { Id: Guid
      TenantId: Guid
      CounterpartyId: Guid
      Reference: string
      Title: string
      Source: string
      ExternalRef: string option
      Currency: string
      EffectiveDate: DateOnly
      ExpiryDate: DateOnly option
      Status: string
      DocumentUrl: string option }

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

    let private selectSql =
        """
        select id, tenant_id, counterparty_id, reference, title, currency, status
        from contracts
        """

    let list (dataSource: NpgsqlDataSource) (scope: TenantScope) : Task<ContractRow list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    selectSql
                    + """
                    where tenant_id = @tenant_id
                    order by reference
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<ContractRow>()

            while reader.Read() do
                rows.Add(readContract reader)

            return List.ofSeq rows
        }

    let findById (dataSource: NpgsqlDataSource) (scope: TenantScope) (contractId: Guid) : Task<ContractRow option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    selectSql
                    + """
                    where tenant_id = @tenant_id and id = @id
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "id" contractId
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readContract reader)
            else
                return None
        }

    let findByReference
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (reference: string)
        : Task<ContractRow option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    selectSql
                    + """
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

    let create (dataSource: NpgsqlDataSource) (scope: TenantScope) (contract: NewContract) : Task<ContractRow option> =
        task {
            if contract.TenantId <> TenantScope.value scope then
                invalidArg (nameof contract) "Contract tenant must match tenant scope."

            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into contracts (
                        id,
                        tenant_id,
                        counterparty_id,
                        reference,
                        title,
                        source,
                        external_ref,
                        currency,
                        effective_date,
                        expiry_date,
                        status,
                        document_url,
                        created_at,
                        updated_at
                    )
                    select
                        @id,
                        @tenant_id,
                        @counterparty_id,
                        @reference,
                        @title,
                        @source,
                        @external_ref,
                        @currency,
                        @effective_date,
                        @expiry_date,
                        @status,
                        @document_url,
                        @created_at,
                        @created_at
                    where exists (
                        select 1 from counterparties
                        where tenant_id = @tenant_id and id = @counterparty_id
                    )
                    returning id, tenant_id, counterparty_id, reference, title, currency, status
                    """,
                    connection
                )

            Sql.addParameter command "id" contract.Id
            Sql.addParameter command "tenant_id" contract.TenantId
            Sql.addParameter command "counterparty_id" contract.CounterpartyId
            Sql.addParameter command "reference" contract.Reference
            Sql.addParameter command "title" contract.Title
            Sql.addParameter command "source" contract.Source
            Sql.addOptionalParameter command "external_ref" (contract.ExternalRef |> Option.map box)
            Sql.addParameter command "currency" contract.Currency
            Sql.addParameter command "effective_date" contract.EffectiveDate
            Sql.addOptionalParameter command "expiry_date" (contract.ExpiryDate |> Option.map box)
            Sql.addParameter command "status" contract.Status
            Sql.addOptionalParameter command "document_url" (contract.DocumentUrl |> Option.map box)
            Sql.addParameter command "created_at" DateTimeOffset.UtcNow
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readContract reader)
            else
                return None
        }
