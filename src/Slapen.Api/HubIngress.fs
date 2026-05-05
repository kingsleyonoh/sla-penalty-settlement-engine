namespace Slapen.Api

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Npgsql
open Slapen.Application
open Slapen.Data

[<RequireQualifiedAccess>]
module HubIngress =
    let private badSignature: HttpHandler =
        setStatusCode StatusCodes.Status401Unauthorized
        >=> json {| error = "invalid_hub_signature" |}

    let private hex (bytes: byte array) =
        Convert.ToHexString bytes |> fun value -> value.ToLowerInvariant()

    let private verify (secret: string) (body: string) (signature: string) =
        if String.IsNullOrWhiteSpace secret || String.IsNullOrWhiteSpace signature then
            false
        else
            let expected =
                use hmac = new HMACSHA256(Encoding.UTF8.GetBytes secret)
                "sha256=" + hex (hmac.ComputeHash(Encoding.UTF8.GetBytes body))

            let actualBytes = Encoding.UTF8.GetBytes signature
            let expectedBytes = Encoding.UTF8.GetBytes expected

            actualBytes.Length = expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes)

    let private parseEnvelope (body: string) : ExternalBreachInput * Guid =
        use document = JsonDocument.Parse body
        let root = document.RootElement
        let payload = root.GetProperty "payload"

        { SourceRef = root.GetProperty("event_id").GetString()
          ContractRef = payload.GetProperty("contract_ref").GetString()
          ClauseRef = payload.GetProperty("clause_ref").GetString()
          MetricValue = payload.GetProperty("metric_value").GetDecimal()
          UnitsMissed =
            let mutable value = Unchecked.defaultof<JsonElement>

            if
                payload.TryGetProperty("units_missed", &value)
                && value.ValueKind <> JsonValueKind.Null
            then
                Some(value.GetDecimal())
            else
                None
          ObservedAt = payload.GetProperty("observed_at").GetDateTimeOffset()
          ReportedAt = payload.GetProperty("reported_at").GetDateTimeOffset()
          RawPayloadJson = body },
        root.GetProperty("tenant_id").GetGuid()

    let ingest: HttpHandler =
        fun next ctx ->
            task {
                let configuration = ctx.GetService<IConfiguration>()
                let secret = configuration["HUB_INGRESS_SECRET"]
                use reader = new StreamReader(ctx.Request.Body, Encoding.UTF8)
                let! body = reader.ReadToEndAsync()

                let signature =
                    match ctx.Request.Headers.TryGetValue "X-Hub-Signature" with
                    | true, values when values.Count > 0 -> string values[0]
                    | _ -> ""

                if not (verify secret body signature) then
                    return! badSignature next ctx
                else
                    try
                        let input, tenantId = parseEnvelope body
                        let dataSource = ctx.GetService<NpgsqlDataSource>()
                        let scope = TenantScope.create tenantId
                        let! tenant = TenantsRepository.findByScope dataSource scope

                        match tenant with
                        | None -> return! Handlers.Response.notFound next ctx
                        | Some _ ->
                            let! result = Ingestion.ingestExternal dataSource scope "hub_ingress" [ input ]

                            match result.BreachIds with
                            | breachId :: _ ->
                                return!
                                    Handlers.Response.created
                                        $"/api/breaches/{breachId}"
                                        {| status = "ingested"
                                           breachId = breachId |}
                                        next
                                        ctx
                            | [] ->
                                return!
                                    json
                                        {| status = "duplicate_or_unresolved"
                                           stored = result.Stored
                                           rejected = result.Rejected |}
                                        next
                                        ctx
                    with :? JsonException as error ->
                        return! Handlers.Response.badRequest error.Message next ctx
            }
