namespace Slapen.Api.Handlers

open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql

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
