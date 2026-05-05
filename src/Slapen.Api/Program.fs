namespace Slapen.Api

open System
open Giraffe
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Npgsql
open Prometheus
open Sentry
open Serilog
open Serilog.Formatting.Json
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
        Log.Logger <- LoggerConfiguration().Enrich.FromLogContext().WriteTo.Console(JsonFormatter()).CreateLogger()

        builder.Host.UseSerilog() |> ignore

        (builder.WebHost :> IWebHostBuilder)
            .UseSentry(fun (options: Sentry.AspNetCore.SentryAspNetCoreOptions) ->
                options.Dsn <- builder.Configuration["SENTRY_DSN"] |> Option.ofObj |> Option.defaultValue ""
                options.Environment <- builder.Environment.EnvironmentName)
        |> ignore

        builder.Services.AddGiraffe() |> ignore
        builder.Services.AddSingleton<RateLimitStore>() |> ignore

        builder.Services.AddSingleton<NpgsqlDataSource>(fun _ -> NpgsqlDataSource.Create databaseUrl)
        |> ignore

        let app = builder.Build()

        app.UseMiddleware<RequestIdMiddleware>() |> ignore
        app.UseMiddleware<RateLimitingMiddleware>() |> ignore
        app.UseMiddleware<MetricsAuthMiddleware>() |> ignore

        if builder.Configuration["PROMETHEUS_ENABLED"] <> "false" then
            app.UseHttpMetrics() |> ignore
            app.MapMetrics("/metrics") |> ignore

        app.UseStaticFiles() |> ignore
        app.UseMiddleware<TenantAuthMiddleware>() |> ignore
        app.UseGiraffe Routes.app
        app

    [<EntryPoint>]
    let main args =
        let app = buildApp args
        app.Run()
        0
