namespace Slapen.Api.Middleware

open System
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Data

type TenantContext =
    { Scope: TenantScope
      Tenant: TenantRecord }

[<RequireQualifiedAccess>]
module TenantAuth =
    [<Literal>]
    let ItemKey = "Slapen.Tenant"

    let tryGetTenant (ctx: HttpContext) =
        match ctx.Items.TryGetValue ItemKey with
        | true, (:? TenantContext as tenant) -> Some tenant
        | _ -> None

    let requireTenant ctx =
        tryGetTenant ctx
        |> Option.defaultWith (fun () -> invalidOp "Tenant context is missing.")

    let requireScope ctx = (requireTenant ctx).Scope

    let private hash (apiKey: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes apiKey)
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let private prefix (apiKey: string) =
        if String.IsNullOrWhiteSpace apiKey || apiKey.Length < 12 then
            None
        else
            Some(apiKey.Substring(0, 12))

    let resolveTenant dataSource apiKey =
        task {
            match prefix apiKey with
            | None -> return None
            | Some apiKeyPrefix -> return! TenantsRepository.findByApiKeyHash dataSource apiKeyPrefix (hash apiKey)
        }

type TenantAuthMiddleware(next: RequestDelegate) =
    let isPublicPath (path: PathString) =
        path.StartsWithSegments(PathString "/api/health")

    member _.InvokeAsync(ctx: HttpContext, dataSource: NpgsqlDataSource) : Task =
        task {
            if isPublicPath ctx.Request.Path then
                do! next.Invoke ctx
            else
                match ctx.Request.Headers.TryGetValue "X-API-Key" with
                | false, _ ->
                    ctx.Response.StatusCode <- StatusCodes.Status401Unauthorized
                    do! ctx.Response.WriteAsJsonAsync {| error = "missing_api_key" |}
                | true, values ->
                    let! tenant = TenantAuth.resolveTenant dataSource values[0]

                    match tenant with
                    | None ->
                        ctx.Response.StatusCode <- StatusCodes.Status401Unauthorized
                        do! ctx.Response.WriteAsJsonAsync {| error = "invalid_api_key" |}
                    | Some tenant ->
                        ctx.Items[TenantAuth.ItemKey] <-
                            { Scope = TenantScope.create tenant.Id
                              Tenant = tenant }

                        do! next.Invoke ctx
        }
