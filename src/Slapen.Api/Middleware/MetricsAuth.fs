namespace Slapen.Api.Middleware

open System
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration

type MetricsAuthMiddleware(next: RequestDelegate, configuration: IConfiguration) =
    let requiredUser = configuration["METRICS_BASIC_AUTH_USER"]
    let requiredPass = configuration["METRICS_BASIC_AUTH_PASS"]

    let configured =
        not (String.IsNullOrWhiteSpace requiredUser)
        && not (String.IsNullOrWhiteSpace requiredPass)

    let unauthorized (ctx: HttpContext) =
        ctx.Response.StatusCode <- StatusCodes.Status401Unauthorized
        ctx.Response.Headers.WWWAuthenticate <- "Basic realm=\"slapen-metrics\""
        ctx.Response.WriteAsync "metrics_auth_required"

    let authorized (header: string) =
        if not configured then
            false
        elif
            String.IsNullOrWhiteSpace header
            || not (header.StartsWith("Basic ", StringComparison.Ordinal))
        then
            false
        else
            try
                let encoded = header.Substring("Basic ".Length)
                let decoded = Encoding.UTF8.GetString(Convert.FromBase64String encoded)
                decoded = $"{requiredUser}:{requiredPass}"
            with _ ->
                false

    member _.InvokeAsync(ctx: HttpContext) : Task =
        task {
            if ctx.Request.Path.StartsWithSegments(PathString "/metrics") then
                match ctx.Request.Headers.TryGetValue "Authorization" with
                | true, values when authorized values[0] -> do! next.Invoke ctx
                | _ -> do! unauthorized ctx
            else
                do! next.Invoke ctx
        }
