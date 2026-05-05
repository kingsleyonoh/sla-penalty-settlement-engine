namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type TenantRecord =
    { Id: Guid
      Name: string
      Slug: string
      LegalName: string
      FullLegalName: string
      DisplayName: string
      AddressJson: string
      RegistrationJson: string
      ContactJson: string
      WordmarkUrl: string option
      BrandPrimaryHex: string option
      BrandAccentHex: string option
      Locale: string
      Timezone: string
      DefaultCurrency: string }

[<RequireQualifiedAccess>]
module TenantsRepository =
    let private selectTenant =
        """
        select
            id,
            name,
            slug,
            legal_name,
            full_legal_name,
            display_name,
            address::text as address_json,
            registration::text as registration_json,
            contact::text as contact_json,
            wordmark_url,
            brand_primary_hex,
            brand_accent_hex,
            locale,
            timezone,
            default_currency
        from tenants
        """

    let private readTenant (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          Name = reader.GetString(reader.GetOrdinal "name")
          Slug = reader.GetString(reader.GetOrdinal "slug")
          LegalName = reader.GetString(reader.GetOrdinal "legal_name")
          FullLegalName = reader.GetString(reader.GetOrdinal "full_legal_name")
          DisplayName = reader.GetString(reader.GetOrdinal "display_name")
          AddressJson = reader.GetString(reader.GetOrdinal "address_json")
          RegistrationJson = reader.GetString(reader.GetOrdinal "registration_json")
          ContactJson = reader.GetString(reader.GetOrdinal "contact_json")
          WordmarkUrl = Sql.stringOption reader "wordmark_url"
          BrandPrimaryHex = Sql.stringOption reader "brand_primary_hex"
          BrandAccentHex = Sql.stringOption reader "brand_accent_hex"
          Locale = reader.GetString(reader.GetOrdinal "locale")
          Timezone = reader.GetString(reader.GetOrdinal "timezone")
          DefaultCurrency = reader.GetString(reader.GetOrdinal "default_currency") }

    let findByScope (dataSource: NpgsqlDataSource) (scope: TenantScope) : Task<TenantRecord option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(selectTenant + " where id = @tenant_id and is_active = true", connection)

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            use! reader = command.ExecuteReaderAsync()

            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readTenant reader)
            else
                return None
        }

    let findByApiKeyHash
        (dataSource: NpgsqlDataSource)
        (apiKeyPrefix: string)
        (apiKeyHash: string)
        : Task<TenantRecord option> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    selectTenant
                    + " where api_key_prefix = @api_key_prefix and api_key_hash = @api_key_hash and is_active = true",
                    connection
                )

            Sql.addParameter command "api_key_prefix" apiKeyPrefix
            Sql.addParameter command "api_key_hash" apiKeyHash
            use! reader = command.ExecuteReaderAsync()

            let! hasRow = reader.ReadAsync()

            if hasRow then
                return Some(readTenant reader)
            else
                return None
        }
