namespace Slapen.Tenants

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data

type TenantSnapshot =
    { TenantId: Guid
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
      DefaultCurrency: string
      CapturedAt: DateTimeOffset }

[<RequireQualifiedAccess>]
module TenantSnapshotter =
    let capture (dataSource: NpgsqlDataSource) (scope: TenantScope) : Task<TenantSnapshot option> =
        task {
            let! tenant = TenantsRepository.findByScope dataSource scope
            let capturedAt = DateTimeOffset.UtcNow

            return
                tenant
                |> Option.map (fun row ->
                    { TenantId = row.Id
                      Name = row.Name
                      Slug = row.Slug
                      LegalName = row.LegalName
                      FullLegalName = row.FullLegalName
                      DisplayName = row.DisplayName
                      AddressJson = row.AddressJson
                      RegistrationJson = row.RegistrationJson
                      ContactJson = row.ContactJson
                      WordmarkUrl = row.WordmarkUrl
                      BrandPrimaryHex = row.BrandPrimaryHex
                      BrandAccentHex = row.BrandAccentHex
                      Locale = row.Locale
                      Timezone = row.Timezone
                      DefaultCurrency = row.DefaultCurrency
                      CapturedAt = capturedAt })
        }
