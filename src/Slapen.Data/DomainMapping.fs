namespace Slapen.Data

open System
open Slapen.Domain

[<RequireQualifiedAccess>]
module DomainMapping =
    let ledgerEntryKindText =
        function
        | LedgerEntryKind.Accrual -> "accrual"
        | LedgerEntryKind.Reversal -> "reversal"
        | LedgerEntryKind.Adjustment -> "adjustment"

    let ledgerDirectionText =
        function
        | LedgerDirection.CreditOwedToUs -> "credit_owed_to_us"
        | LedgerDirection.Mirror -> "mirror"

    let reasonCodeText =
        function
        | ReasonCode.SlaBreach -> "sla_breach"
        | ReasonCode.DisputeResolvedInOurFavor -> "dispute_resolved_in_our_favor"
        | ReasonCode.DisputeResolvedAgainst -> "dispute_resolved_against"
        | ReasonCode.OperatorCorrection -> "operator_correction"
        | ReasonCode.ContractCapApplied -> "contract_cap_applied"
        | ReasonCode.WithdrawnBySource -> "withdrawn_by_source"

    let createdByParts =
        function
        | CreatedBy.System -> "system", None
        | CreatedBy.Adapter -> "adapter", None
        | CreatedBy.User userId -> "user", Some userId

    let ledgerEntryKindFromText =
        function
        | "accrual" -> LedgerEntryKind.Accrual
        | "reversal" -> LedgerEntryKind.Reversal
        | "adjustment" -> LedgerEntryKind.Adjustment
        | value -> failwith $"Unknown ledger entry kind '{value}'."

    let ledgerDirectionFromText =
        function
        | "credit_owed_to_us" -> LedgerDirection.CreditOwedToUs
        | "mirror" -> LedgerDirection.Mirror
        | value -> failwith $"Unknown ledger direction '{value}'."

    let reasonCodeFromText =
        function
        | "sla_breach" -> ReasonCode.SlaBreach
        | "dispute_resolved_in_our_favor" -> ReasonCode.DisputeResolvedInOurFavor
        | "dispute_resolved_against" -> ReasonCode.DisputeResolvedAgainst
        | "operator_correction" -> ReasonCode.OperatorCorrection
        | "contract_cap_applied" -> ReasonCode.ContractCapApplied
        | "withdrawn_by_source" -> ReasonCode.WithdrawnBySource
        | value -> failwith $"Unknown reason code '{value}'."

    let createdByFromText kind (userId: Guid option) =
        match kind, userId with
        | "system", _ -> CreatedBy.System
        | "adapter", _ -> CreatedBy.Adapter
        | "user", Some id -> CreatedBy.User id
        | "user", None -> failwith "Ledger row created_by_kind=user requires created_by_user_id."
        | value, _ -> failwith $"Unknown created_by_kind '{value}'."

    let money cents currency =
        match Money.create cents currency with
        | Ok amount -> amount
        | Error error -> failwithf "Invalid persisted money value: %A" error
