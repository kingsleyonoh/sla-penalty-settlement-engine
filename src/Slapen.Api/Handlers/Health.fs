namespace Slapen.Api.Handlers

open System
open System.Threading.Tasks
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Npgsql
open Slapen.Api.Middleware
open Slapen.Data
open StackExchange.Redis

[<RequireQualifiedAccess>]
module Health =
    let live: HttpHandler =
        fun next ctx -> task { return! json {| status = "ok" |} next ctx }

    let db: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()

                try
                    use! connection = dataSource.OpenConnectionAsync().AsTask()
                    use command = new NpgsqlCommand("select 1", connection)
                    let! _ = command.ExecuteScalarAsync()
                    return! json {| status = "ok" |} next ctx
                with _ ->
                    return!
                        (setStatusCode StatusCodes.Status503ServiceUnavailable
                         >=> json {| status = "unavailable" |})
                            next
                            ctx
            }

    let private redis (configuration: IConfiguration) =
        task {
            let url = configuration["REDIS_URL"]

            if String.IsNullOrWhiteSpace url then
                return false
            else
                try
                    use! connection = ConnectionMultiplexer.ConnectAsync url
                    let database = connection.GetDatabase()
                    let! pong = database.PingAsync()
                    return pong >= TimeSpan.Zero
                with _ ->
                    return false
        }

    let private jobs (configuration: IConfiguration) =
        match Int32.TryParse(configuration["OUTBOX_POLL_INTERVAL_SECONDS"]) with
        | true, value -> value > 0
        | false, _ -> true

    let ready: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let configuration = ctx.GetService<IConfiguration>()
                let dbTask = ReadinessRepository.db dataSource
                let redisTask = redis configuration
                let outboxTask = ReadinessRepository.outbox dataSource

                let adapterTask =
                    match TenantAuth.tryGetTenant ctx with
                    | Some tenant -> ReadinessRepository.adapters dataSource tenant.Scope DateTimeOffset.UtcNow
                    | None -> Task.FromResult []

                let! dbOk = dbTask
                let! redisOk = redisTask
                let! outboxOk = outboxTask
                let! adapters = adapterTask
                let jobsOk = jobs configuration
                let adaptersOk = adapters |> List.forall _.Healthy
                let ready = dbOk && redisOk && outboxOk && jobsOk && adaptersOk
                let status = if ready then "ok" else "unavailable"

                let statusCode =
                    if ready then
                        StatusCodes.Status200OK
                    else
                        StatusCodes.Status503ServiceUnavailable

                return!
                    (setStatusCode statusCode
                     >=> json
                             {| status = status
                                db = dbOk
                                redis = redisOk
                                outbox = outboxOk
                                jobs = jobsOk
                                adaptersHealthy = adaptersOk |})
                        next
                        ctx
            }
