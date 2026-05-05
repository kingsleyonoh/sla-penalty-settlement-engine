namespace Slapen.Ecosystem

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks

type ContractLifecycleBreach =
    { SourceRef: string
      ContractRef: string
      ClauseRef: string
      MetricValue: decimal
      UnitsMissed: decimal option
      ObservedAt: DateTimeOffset
      ReportedAt: DateTimeOffset
      RawPayloadJson: string }

type ContractLifecycleClient = private { Http: EcosystemHttpClient }

[<RequireQualifiedAccess>]
module ContractLifecycleClient =
    let create httpClient config =
        { Http = EcosystemHttpClient.create httpClient config }

    let isEnabled client =
        EcosystemHttpClient.isEnabled client.Http

    let private stringProperty (element: JsonElement) (name: string) = element.GetProperty(name).GetString()

    let private optionalDecimalProperty (element: JsonElement) (name: string) =
        let mutable value = Unchecked.defaultof<JsonElement>

        if element.TryGetProperty(name, &value) && value.ValueKind <> JsonValueKind.Null then
            Some(value.GetDecimal())
        else
            None

    let parseBreach (raw: string) =
        use document = JsonDocument.Parse raw
        let root = document.RootElement

        { SourceRef = stringProperty root "event_id"
          ContractRef = stringProperty root "contract_ref"
          ClauseRef = stringProperty root "clause_ref"
          MetricValue = root.GetProperty("metric_value").GetDecimal()
          UnitsMissed = optionalDecimalProperty root "units_missed"
          ObservedAt = root.GetProperty("observed_at").GetDateTimeOffset()
          ReportedAt = root.GetProperty("reported_at").GetDateTimeOffset()
          RawPayloadJson = raw }

    let private parseBreaches body =
        if String.IsNullOrWhiteSpace body then
            []
        else
            use document = JsonDocument.Parse body
            let root = document.RootElement

            let items =
                if root.ValueKind = JsonValueKind.Array then
                    root.EnumerateArray()
                else
                    root.GetProperty("items").EnumerateArray()

            items |> Seq.map (fun item -> parseBreach (item.GetRawText())) |> List.ofSeq

    let fetchBreaches (client: ContractLifecycleClient) (since: DateTimeOffset) : Task<ContractLifecycleBreach list> =
        task {
            if not (isEnabled client) then
                return []
            else
                let sinceValue = WebUtility.UrlEncode(since.UtcDateTime.ToString("O"))
                let path = "/api/obligations?status=breached%2Coverdue&since=" + sinceValue
                let! response = EcosystemHttpClient.get client.Http path

                match response with
                | Ok body -> return parseBreaches body
                | Error error -> return raise (HttpRequestException error)
        }
