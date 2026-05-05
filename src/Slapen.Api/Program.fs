namespace Slapen.Api

open System
open Giraffe
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Npgsql
open Slapen.Api.Middleware

type AppMarker = class end

module Program =
    let private connectionString (builder: WebApplicationBuilder) =
        let value = builder.Configuration["DATABASE_URL"]

        if String.IsNullOrWhiteSpace value then
            failwith "DATABASE_URL is required."
        else
            value

    let buildApp (args: string array) =
        let builder = WebApplication.CreateBuilder(args)
        let databaseUrl = connectionString builder

        builder.Services.AddGiraffe() |> ignore
        builder.Services.AddSingleton<RateLimitStore>() |> ignore

        builder.Services.AddSingleton<NpgsqlDataSource>(fun _ -> NpgsqlDataSource.Create databaseUrl)
        |> ignore

        let app = builder.Build()

        app.UseMiddleware<RequestIdMiddleware>() |> ignore
        app.UseMiddleware<RateLimitingMiddleware>() |> ignore
        app.UseMiddleware<TenantAuthMiddleware>() |> ignore
        app.UseGiraffe Routes.app
        app

    [<EntryPoint>]
    let main args =
        let app = buildApp args
        app.Run()
        0
