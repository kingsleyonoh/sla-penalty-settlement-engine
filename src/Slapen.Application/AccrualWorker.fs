namespace Slapen.Application

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module AccrualWorker =
    type Outcome =
        | Accrued of ledgerEntryIds: Guid list
        | NoPenalty of reason: NoPenaltyReason
        | CalculationFailed of error: DomainError
        | NotAccruable of status: BreachStatus
        | NotFound

    let private accrualStart breach clause =
        match clause.AccrualStartFrom with
        | AccrualStartFrom.BreachObservedAt -> breach.ObservedAt
        | AccrualStartFrom.BreachReportedAt -> breach.ReportedAt
        | AccrualStartFrom.NextBillingPeriodStart ->
            DateTimeOffset(breach.ObservedAt.Year, breach.ObservedAt.Month, 1, 0, 0, 0, breach.ObservedAt.Offset)
                .AddMonths(1)

    let private candidate (context: BreachAccrualContext) direction amount asOf =
        { Id = Guid.NewGuid()
          TenantId = context.Breach.TenantId
          ContractId = context.Contract.Id
          CounterpartyId = context.Contract.CounterpartyId
          SlaClauseId = context.Clause.Id
          BreachEventId = context.Breach.Id
          EntryKind = LedgerEntryKind.Accrual
          Direction = direction
          Amount = amount
          AccrualPeriodStart = accrualStart context.Breach context.Clause
          AccrualPeriodEnd = context.Breach.ResolvedAt |> Option.defaultValue asOf
          CompensatesLedgerId = None
          ReasonCode = ReasonCode.SlaBreach
          ReasonNotes = Some "SLA breach penalty accrual"
          CreatedAt = asOf
          CreatedBy = CreatedBy.System }

    let private accrualAllowed =
        function
        | BreachStatus.Pending
        | BreachStatus.Disputed -> true
        | _ -> false

    let private writeAccrual (dataSource: NpgsqlDataSource) scope (context: BreachAccrualContext) amount asOf =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()
            use! transaction = connection.BeginTransactionAsync()

            let credit = candidate context LedgerDirection.CreditOwedToUs amount asOf
            let mirror = candidate context LedgerDirection.Mirror amount asOf

            try
                let! writeResult = LedgerWriter.writePairWithinTransaction connection transaction scope credit mirror

                match writeResult with
                | Error error ->
                    do! transaction.RollbackAsync()
                    return Outcome.CalculationFailed error
                | Ok ids ->
                    let! updated =
                        BreachEventsRepository.updateStatus
                            connection
                            transaction
                            scope
                            context.Breach.Id
                            context.Breach.Status
                            BreachStatus.Accrued
                            asOf

                    if updated then
                        do! transaction.CommitAsync()
                        return Outcome.Accrued ids
                    else
                        do! transaction.RollbackAsync()
                        return Outcome.NotAccruable context.Breach.Status
            with error ->
                do! transaction.RollbackAsync()
                return raise error
        }

    let processBreach
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        (asOf: DateTimeOffset)
        : Task<Outcome> =
        task {
            let! context = BreachEventsRepository.findAccrualContext dataSource scope breachEventId

            match context with
            | None -> return Outcome.NotFound
            | Some context when not (accrualAllowed context.Breach.Status) ->
                return Outcome.NotAccruable context.Breach.Status
            | Some context ->
                let input =
                    { Contract = context.Contract
                      Clause = context.Clause
                      Breach = context.Breach
                      PreviousAccruals = context.PreviousAccruals
                      PriorMeasurementWindowBreachCount = context.PriorMeasurementWindowBreachCount
                      AsOf = asOf }

                match RulesEngine.calculatePenalty input with
                | Error error -> return Outcome.CalculationFailed error
                | Ok(PenaltyResult.NoPenalty reason) -> return Outcome.NoPenalty reason
                | Ok(PenaltyResult.Accrued amount)
                | Ok(PenaltyResult.CapApplied(amount, _)) -> return! writeAccrual dataSource scope context amount asOf
        }
