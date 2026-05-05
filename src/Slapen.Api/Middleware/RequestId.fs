namespace Slapen.Api.Middleware

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type RequestIdMiddleware(next: RequestDelegate) =
    member _.InvokeAsync(ctx: HttpContext) : Task =
        task {
            let requestIdHeader = "X-Request-ID"

            let requestId =
                match ctx.Request.Headers.TryGetValue requestIdHeader with
                | true, values when values.Count > 0 && not (String.IsNullOrWhiteSpace values[0]) -> values[0]
                | _ -> Guid.NewGuid().ToString("N")

            ctx.TraceIdentifier <- requestId
            ctx.Items[requestIdHeader] <- requestId
            ctx.Response.Headers[requestIdHeader] <- requestId
            do! next.Invoke ctx
        }
