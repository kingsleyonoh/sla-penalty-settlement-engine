namespace Slapen.Api.Middleware

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration

type private RateWindow =
    { StartsAt: DateTimeOffset; Count: int }

type RateLimitStore() =
    let windows = ConcurrentDictionary<string, RateWindow>()

    member _.TryIncrement(key: string, limit: int, now: DateTimeOffset) =
        let minuteStart =
            DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset)

        let initial = { StartsAt = minuteStart; Count = 1 }

        let update (_: string) existing =
            if existing.StartsAt = minuteStart then
                { existing with
                    Count = existing.Count + 1 }
            else
                initial

        let window = windows.AddOrUpdate(key, initial, update)
        window.Count <= limit

type RateLimitingMiddleware(next: RequestDelegate) =
    member _.InvokeAsync(ctx: HttpContext, store: RateLimitStore, configuration: IConfiguration) : Task =
        task {
            let limit =
                match Int32.TryParse(configuration["SLAPEN_RATE_LIMIT_PER_MINUTE"]) with
                | true, value when value > 0 -> value
                | _ -> 120

            let key =
                match ctx.Request.Headers.TryGetValue "X-API-Key" with
                | true, values when values.Count > 0 -> string values[0]
                | _ -> string ctx.Connection.RemoteIpAddress

            if store.TryIncrement(key, limit, DateTimeOffset.UtcNow) then
                do! next.Invoke ctx
            else
                ctx.Response.StatusCode <- StatusCodes.Status429TooManyRequests
                do! ctx.Response.WriteAsJsonAsync {| error = "rate_limited" |}
        }
