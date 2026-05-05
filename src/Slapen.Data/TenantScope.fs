namespace Slapen.Data

open System

[<Struct>]
type TenantScope = private TenantScope of Guid

[<RequireQualifiedAccess>]
module TenantScope =
    let create tenantId =
        if tenantId = Guid.Empty then
            invalidArg (nameof tenantId) "Tenant scope cannot be empty."

        TenantScope tenantId

    let value (TenantScope tenantId) = tenantId
