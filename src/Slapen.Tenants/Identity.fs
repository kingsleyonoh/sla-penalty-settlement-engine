namespace Slapen.Tenants

open System
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Npgsql
open Slapen.Data

type ResolvedTenantIdentity =
    { TenantId: Guid
      Slug: string
      DisplayName: string
      DefaultCurrency: string }

[<RequireQualifiedAccess>]
module ApiKey =
    let prefixLength = 12

    let prefix (rawApiKey: string) =
        if String.IsNullOrWhiteSpace rawApiKey then
            invalidArg (nameof rawApiKey) "API key cannot be blank."

        rawApiKey.Substring(0, Math.Min(prefixLength, rawApiKey.Length))

    let sha256 (rawApiKey: string) =
        if String.IsNullOrWhiteSpace rawApiKey then
            invalidArg (nameof rawApiKey) "API key cannot be blank."

        rawApiKey
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

[<RequireQualifiedAccess>]
module Identity =
    let resolveApiKey (dataSource: NpgsqlDataSource) (rawApiKey: string) : Task<ResolvedTenantIdentity option> =
        task {
            let keyPrefix = ApiKey.prefix rawApiKey
            let keyHash = ApiKey.sha256 rawApiKey
            let! tenant = TenantsRepository.findByApiKeyHash dataSource keyPrefix keyHash

            return
                tenant
                |> Option.map (fun row ->
                    { TenantId = row.Id
                      Slug = row.Slug
                      DisplayName = row.DisplayName
                      DefaultCurrency = row.DefaultCurrency })
        }
