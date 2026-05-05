namespace Slapen.Application

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data
open Slapen.Domain

[<RequireQualifiedAccess>]
module ReversalEngine =
    type Outcome =
        | Reversed of ledgerEntryIds: Guid list
        | InvalidTransition of fromStatus: BreachStatus * toStatus: BreachStatus
        | NoAccrualsToReverse
        | WriteFailed of error: DomainError
        | NotFound

    let private reversalTargetAllowed =
        function
        | BreachStatus.Disputed
        | BreachStatus.Withdrawn
        | BreachStatus.Superseded -> true
        | _ -> false

    let private uncompensatedCreditAccruals rows =
        let compensated =
            rows
            |> List.choose (fun row ->
                if row.EntryKind = LedgerEntryKind.Reversal then
                    row.CompensatesLedgerId
                else
                    None)
            |> Set.ofList

        rows
        |> List.filter (fun row ->
            row.EntryKind = LedgerEntryKind.Accrual
            && row.Direction = LedgerDirection.CreditOwedToUs
            && not (compensated.Contains row.Id))

    let private reversalCandidate direction reason notes createdBy asOf (accrual: LedgerEntryCandidate) =
        { Id = Guid.NewGuid()
          TenantId = accrual.TenantId
          ContractId = accrual.ContractId
          CounterpartyId = accrual.CounterpartyId
          SlaClauseId = accrual.SlaClauseId
          BreachEventId = accrual.BreachEventId
          EntryKind = LedgerEntryKind.Reversal
          Direction = direction
          Amount = accrual.Amount
          AccrualPeriodStart = accrual.AccrualPeriodStart
          AccrualPeriodEnd = accrual.AccrualPeriodEnd
          CompensatesLedgerId = Some accrual.Id
          ReasonCode = reason
          ReasonNotes = notes
          CreatedAt = asOf
          CreatedBy = createdBy }

    let private writeReversals
        (dataSource: NpgsqlDataSource)
        scope
        (context: BreachAccrualContext)
        targetStatus
        reason
        notes
        createdBy
        asOf
        accruals
        =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()
            use! transaction = connection.BeginTransactionAsync()

            try
                let writtenIds = ResizeArray<Guid>()
                let mutable failed: DomainError option = None

                for accrual in accruals do
                    match failed with
                    | Some _ -> ()
                    | None ->
                        let credit =
                            reversalCandidate LedgerDirection.CreditOwedToUs reason notes createdBy asOf accrual

                        let mirror =
                            reversalCandidate LedgerDirection.Mirror reason notes createdBy asOf accrual

                        let! result = LedgerWriter.writePairWithinTransaction connection transaction scope credit mirror

                        match result with
                        | Ok ids -> ids |> List.iter writtenIds.Add
                        | Error error -> failed <- Some error

                match failed with
                | Some error ->
                    do! transaction.RollbackAsync()
                    return Outcome.WriteFailed error
                | None ->
                    let! updated =
                        BreachEventsRepository.updateStatus
                            connection
                            transaction
                            scope
                            context.Breach.Id
                            BreachStatus.Accrued
                            targetStatus
                            asOf

                    if updated then
                        do! transaction.CommitAsync()
                        return Outcome.Reversed(List.ofSeq writtenIds)
                    else
                        do! transaction.RollbackAsync()
                        return Outcome.InvalidTransition(context.Breach.Status, targetStatus)
            with error ->
                do! transaction.RollbackAsync()
                return raise error
        }

    let reverseBreach
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        (targetStatus: BreachStatus)
        (reason: ReasonCode)
        (notes: string option)
        (createdBy: CreatedBy)
        (asOf: DateTimeOffset)
        : Task<Outcome> =
        task {
            let! context = BreachEventsRepository.findAccrualContext dataSource scope breachEventId

            match context with
            | None -> return Outcome.NotFound
            | Some context when
                context.Breach.Status <> BreachStatus.Accrued
                || not (reversalTargetAllowed targetStatus)
                ->
                return Outcome.InvalidTransition(context.Breach.Status, targetStatus)
            | Some context ->
                let accruals = uncompensatedCreditAccruals context.PreviousAccruals

                if List.isEmpty accruals then
                    return Outcome.NoAccrualsToReverse
                else
                    return! writeReversals dataSource scope context targetStatus reason notes createdBy asOf accruals
        }
