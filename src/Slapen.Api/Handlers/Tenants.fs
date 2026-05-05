namespace Slapen.Api.Handlers

open Giraffe
open Slapen.Api.Middleware

[<RequireQualifiedAccess>]
module Tenants =
    let me: HttpHandler =
        fun next ctx ->
            task {
                let tenant = TenantAuth.requireTenant ctx

                return!
                    json
                        {| id = tenant.Tenant.Id
                           name = tenant.Tenant.Name
                           slug = tenant.Tenant.Slug
                           displayName = tenant.Tenant.DisplayName
                           locale = tenant.Tenant.Locale
                           timezone = tenant.Tenant.Timezone
                           defaultCurrency = tenant.Tenant.DefaultCurrency |}
                        next
                        ctx
            }
