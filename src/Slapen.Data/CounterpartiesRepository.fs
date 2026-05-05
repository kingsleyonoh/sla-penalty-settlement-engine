namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type CounterpartyRow =
    { Id: Guid
      TenantId: Guid
      CanonicalName: string
      NormalizedName: string
      TaxId: string option
      CountryCode: string option
      ExternalRefsJson: string
      DefaultCurrency: string option }

type NewCounterparty =
    { Id: Guid
      TenantId: Guid
      CanonicalName: string
      TaxId: string option
      CountryCode: string option
      ExternalRefsJson: string
      DefaultCurrency: string option }

[<RequireQualifiedAccess>]
module CounterpartiesRepository =
    let private readCounterparty (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          CanonicalName = reader.GetString(reader.GetOrdinal "canonical_name")
          NormalizedName = reader.GetString(reader.GetOrdinal "normalized_name")
          TaxId = Sql.stringOption reader "tax_id"
          CountryCode = Sql.stringOption reader "country_code"
          ExternalRefsJson = reader.GetString(reader.GetOrdinal "external_refs_json")
          DefaultCurrency = Sql.stringOption reader "default_currency" }

    let private selectSql =
        """
        select
            id,
            tenant_id,
            canonical_name,
            normalized_name::text as normalized_name,
            tax_id,
            country_code,
            external_refs::text as external_refs_json,
            default_currency
        from counterparties
        """

    let list (dataSource: NpgsqlDataSource) (scope: TenantScope) : Task<CounterpartyRow list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    selectSql
                    + """
                    where tenant_id = @tenant_id
                    order by canonical_name
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<CounterpartyRow>()

            while reader.Read() do
                rows.Add(readCounterparty reader)

            return List.ofSeq rows
        }

    let create
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (counterparty: NewCounterparty)
        : Task<CounterpartyRow> =
        task {
            if counterparty.TenantId <> TenantScope.value scope then
                invalidArg (nameof counterparty) "Counterparty tenant must match tenant scope."

            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into counterparties (
                        id,
                        tenant_id,
                        canonical_name,
                        normalized_name,
                        tax_id,
                        country_code,
                        external_refs,
                        default_currency,
                        created_at,
                        updated_at
                    )
                    values (
                        @id,
                        @tenant_id,
                        @canonical_name,
                        @normalized_name,
                        @tax_id,
                        @country_code,
                        cast(@external_refs as jsonb),
                        @default_currency,
                        @created_at,
                        @created_at
                    )
                    returning
                        id,
                        tenant_id,
                        canonical_name,
                        normalized_name::text as normalized_name,
                        tax_id,
                        country_code,
                        external_refs::text as external_refs_json,
                        default_currency
                    """,
                    connection
                )

            Sql.addParameter command "id" counterparty.Id
            Sql.addParameter command "tenant_id" counterparty.TenantId
            Sql.addParameter command "canonical_name" counterparty.CanonicalName
            Sql.addParameter command "normalized_name" (counterparty.CanonicalName.Trim().ToLowerInvariant())
            Sql.addOptionalParameter command "tax_id" (counterparty.TaxId |> Option.map box)
            Sql.addOptionalParameter command "country_code" (counterparty.CountryCode |> Option.map box)
            Sql.addParameter command "external_refs" counterparty.ExternalRefsJson
            Sql.addOptionalParameter command "default_currency" (counterparty.DefaultCurrency |> Option.map box)
            Sql.addParameter command "created_at" DateTimeOffset.UtcNow
            use! reader = command.ExecuteReaderAsync()
            let! hasRow = reader.ReadAsync()

            if hasRow then
                return readCounterparty reader
            else
                return failwith "Counterparty insert did not return a row."
        }
