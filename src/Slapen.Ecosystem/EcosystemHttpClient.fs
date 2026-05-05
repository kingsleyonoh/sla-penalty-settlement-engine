namespace Slapen.Ecosystem

open System
open System.Net.Http
open System.Text
open System.Threading.Tasks

type EcosystemHttpClientConfig =
    { BaseUrl: string option
      ApiKey: string option
      Enabled: bool }

type EcosystemHttpClient =
    private
        { HttpClient: HttpClient
          Config: EcosystemHttpClientConfig }

[<RequireQualifiedAccess>]
module EcosystemHttpClient =
    let create (httpClient: HttpClient) (config: EcosystemHttpClientConfig) =
        match config.BaseUrl with
        | Some baseUrl when not (String.IsNullOrWhiteSpace baseUrl) && isNull httpClient.BaseAddress ->
            httpClient.BaseAddress <- Uri(baseUrl.TrimEnd('/'))
        | _ -> ()

        { HttpClient = httpClient
          Config = config }

    let isEnabled client =
        client.Config.Enabled
        && Option.exists (String.IsNullOrWhiteSpace >> not) client.Config.BaseUrl
        && Option.exists (String.IsNullOrWhiteSpace >> not) client.Config.ApiKey

    let sendJson
        (client: EcosystemHttpClient)
        (method: HttpMethod)
        (path: string)
        (idempotencyKey: string option)
        (payloadJson: string)
        : Task<Result<string, string>> =
        task {
            if not (isEnabled client) then
                return Ok ""
            else
                use request = new HttpRequestMessage(method, path)
                request.Content <- new StringContent(payloadJson, Encoding.UTF8, "application/json")
                request.Headers.Add("X-API-Key", string client.Config.ApiKey.Value)

                match idempotencyKey with
                | Some value -> request.Headers.Add("Idempotency-Key", string value)
                | None -> ()

                use! response = client.HttpClient.SendAsync request
                let! responseBody = response.Content.ReadAsStringAsync()

                if response.IsSuccessStatusCode then
                    return Ok responseBody
                else
                    return Error $"HTTP {(int response.StatusCode)} from {path}: {responseBody}"
        }

    let get (client: EcosystemHttpClient) (path: string) : Task<Result<string, string>> =
        task {
            if not (isEnabled client) then
                return Ok ""
            else
                use request = new HttpRequestMessage(HttpMethod.Get, path)
                request.Headers.Add("X-API-Key", string client.Config.ApiKey.Value)
                use! response = client.HttpClient.SendAsync request
                let! responseBody = response.Content.ReadAsStringAsync()

                if response.IsSuccessStatusCode then
                    return Ok responseBody
                else
                    return Error $"HTTP {(int response.StatusCode)} from {path}: {responseBody}"
        }
