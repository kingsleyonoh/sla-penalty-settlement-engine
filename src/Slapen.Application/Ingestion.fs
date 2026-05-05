namespace Slapen.Application

open System
open System.Globalization
open System.Text.Json
open System.Threading.Tasks
open Npgsql
open Slapen.Data

type ManualIngestionInput =
    { ContractId: Guid
      SlaClauseId: Guid
      SourceRef: string option
      MetricValue: decimal
      UnitsMissed: decimal option
      ObservedAt: DateTimeOffset
      ReportedAt: DateTimeOffset
      RawPayloadJson: string }

type IngestionResult =
    { RunId: Guid
      Status: string
      Attempted: int
      Stored: int
      Rejected: int
      BreachIds: Guid list }

type ExternalBreachInput =
    { SourceRef: string
      ContractRef: string
      ClauseRef: string
      MetricValue: decimal
      UnitsMissed: decimal option
      ObservedAt: DateTimeOffset
      ReportedAt: DateTimeOffset
      RawPayloadJson: string }

[<RequireQualifiedAccess>]
module Ingestion =
    let private fixedHeaders =
        [ "source_ref"
          "contract_id"
          "sla_clause_id"
          "metric_value"
          "units_missed"
          "observed_at"
          "reported_at" ]

    let private finish dataSource scope runId source attempted stored rejected error now breachIds =
        task {
            let status =
                if Option.isSome error || attempted = 0 && rejected > 0 then
                    "failed"
                elif rejected > 0 then
                    "partial"
                else
                    "succeeded"

            let! _ = IngestionRepository.finishRun dataSource scope runId status attempted stored rejected error now

            return
                { RunId = runId
                  Status = status
                  Attempted = attempted
                  Stored = stored
                  Rejected = rejected
                  BreachIds = breachIds }
        }

    let private csvFields (line: string) =
        let fields = ResizeArray<string>()
        let mutable inQuotes = false
        let mutable current = Text.StringBuilder()

        for character in line do
            match character with
            | '"' ->
                inQuotes <- not inQuotes
                current <- current.Append character
            | ',' when not inQuotes ->
                fields.Add(current.ToString().Trim().Trim('"'))
                current <- Text.StringBuilder()
            | _ -> current <- current.Append character

        fields.Add(current.ToString().Trim().Trim('"'))
        List.ofSeq fields

    let private rawPayload sourceRef contractId clauseId metricValue unitsMissed observedAt reportedAt =
        JsonSerializer.Serialize(
            {| source_ref = sourceRef
               contract_id = contractId
               sla_clause_id = clauseId
               metric_value = metricValue
               units_missed = unitsMissed
               observed_at = observedAt
               reported_at = reportedAt |}
        )

    let private parseCsvRow (fields: string list) =
        try
            match fields with
            | [ sourceRef; contractId; clauseId; metricValue; unitsMissed; observedAt; reportedAt ] when
                not (String.IsNullOrWhiteSpace sourceRef)
                ->
                let units =
                    if String.IsNullOrWhiteSpace unitsMissed then
                        None
                    else
                        Some(Decimal.Parse(unitsMissed, CultureInfo.InvariantCulture))

                Ok(
                    { ContractId = Guid.Parse contractId
                      SlaClauseId = Guid.Parse clauseId
                      SourceRef = Some sourceRef
                      MetricValue = Decimal.Parse(metricValue, CultureInfo.InvariantCulture)
                      UnitsMissed = units
                      ObservedAt = DateTimeOffset.Parse(observedAt, CultureInfo.InvariantCulture)
                      ReportedAt = DateTimeOffset.Parse(reportedAt, CultureInfo.InvariantCulture)
                      RawPayloadJson =
                        rawPayload sourceRef contractId clauseId metricValue unitsMissed observedAt reportedAt }
                )
            | _ -> Error "CSV row does not match the fixed breach import schema."
        with error ->
            Error error.Message

    let private store dataSource scope source (input: ManualIngestionInput) =
        task {
            let breach =
                { Id = Guid.NewGuid()
                  TenantId = TenantScope.value scope
                  ContractId = input.ContractId
                  SlaClauseId = input.SlaClauseId
                  Source = source
                  SourceRef = input.SourceRef
                  MetricValue = input.MetricValue
                  UnitsMissed = input.UnitsMissed
                  ObservedAt = input.ObservedAt
                  ReportedAt = input.ReportedAt
                  RawPayloadJson = input.RawPayloadJson }

            return! IngestionRepository.insertBreach dataSource scope breach
        }

    let private storeExternal dataSource scope source (input: ExternalBreachInput) =
        task {
            let! resolution =
                IngestionRepository.resolveContractClause dataSource scope input.ContractRef input.ClauseRef

            match resolution with
            | None -> return None
            | Some resolved ->
                return!
                    store
                        dataSource
                        scope
                        source
                        { ContractId = resolved.ContractId
                          SlaClauseId = resolved.SlaClauseId
                          SourceRef = Some input.SourceRef
                          MetricValue = input.MetricValue
                          UnitsMissed = input.UnitsMissed
                          ObservedAt = input.ObservedAt
                          ReportedAt = input.ReportedAt
                          RawPayloadJson = input.RawPayloadJson }
        }

    let ingestManual
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (input: ManualIngestionInput)
        : Task<IngestionResult> =
        task {
            let now = DateTimeOffset.UtcNow
            let runId = Guid.NewGuid()
            do! IngestionRepository.startRun dataSource scope runId "manual" now
            let! stored = store dataSource scope "manual" input

            match stored with
            | Some breach -> return! finish dataSource scope runId "manual" 1 1 0 None now [ breach.Id ]
            | None -> return! finish dataSource scope runId "manual" 1 0 1 None now []
        }

    let ingestCsv (dataSource: NpgsqlDataSource) (scope: TenantScope) (csv: string) : Task<IngestionResult> =
        task {
            let now = DateTimeOffset.UtcNow
            let runId = Guid.NewGuid()
            do! IngestionRepository.startRun dataSource scope runId "csv_import" now

            let lines =
                csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                |> Array.map _.Trim()
                |> Array.filter (String.IsNullOrWhiteSpace >> not)
                |> Array.toList

            match lines with
            | [] -> return! finish dataSource scope runId "csv_import" 0 0 1 (Some "CSV file is empty.") now []
            | header :: rows when csvFields header <> fixedHeaders ->
                return!
                    finish
                        dataSource
                        scope
                        runId
                        "csv_import"
                        0
                        0
                        1
                        (Some "CSV header must match the fixed breach import schema.")
                        now
                        []
            | _header :: rows ->
                let mutable attempted = 0
                let mutable stored = 0
                let mutable rejected = 0
                let breachIds = ResizeArray<Guid>()

                for row in rows do
                    attempted <- attempted + 1

                    match parseCsvRow (csvFields row) with
                    | Error _ -> rejected <- rejected + 1
                    | Ok input ->
                        let! result = store dataSource scope "csv_import" input

                        match result with
                        | Some breach ->
                            stored <- stored + 1
                            breachIds.Add breach.Id
                        | None -> rejected <- rejected + 1

                return!
                    finish dataSource scope runId "csv_import" attempted stored rejected None now (List.ofSeq breachIds)
        }

    let ingestExternal
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (source: string)
        (inputs: ExternalBreachInput list)
        : Task<IngestionResult> =
        task {
            let now = DateTimeOffset.UtcNow
            let runId = Guid.NewGuid()
            do! IngestionRepository.startRun dataSource scope runId source now
            let mutable stored = 0
            let mutable rejected = 0
            let breachIds = ResizeArray<Guid>()

            for input in inputs do
                let! result = storeExternal dataSource scope source input

                match result with
                | Some breach ->
                    stored <- stored + 1
                    breachIds.Add breach.Id
                | None -> rejected <- rejected + 1

            return! finish dataSource scope runId source inputs.Length stored rejected None now (List.ofSeq breachIds)
        }
