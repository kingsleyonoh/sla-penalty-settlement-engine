namespace Slapen.Audit

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Data

type AuditActor =
    | User of Guid
    | System of string
    | Adapter of string

type AuditRecord =
    { Actor: AuditActor
      Action: string
      EntityKind: string
      EntityId: Guid
      BeforeStateJson: string option
      AfterStateJson: string option }

[<RequireQualifiedAccess>]
module AuditRecorder =
    let private actorParts =
        function
        | User userId -> "user", string userId
        | System actorId -> "system", actorId
        | Adapter actorId -> "adapter", actorId

    let record (dataSource: NpgsqlDataSource) (scope: TenantScope) (record: AuditRecord) : Task<Guid> =
        task {
            let actorKind, actorId = actorParts record.Actor

            let entry: NewAuditLog =
                { Id = Guid.NewGuid()
                  TenantId = TenantScope.value scope
                  ActorKind = actorKind
                  ActorId = actorId
                  Action = record.Action
                  EntityKind = record.EntityKind
                  EntityId = record.EntityId
                  BeforeStateJson = record.BeforeStateJson
                  AfterStateJson = record.AfterStateJson
                  OccurredAt = DateTimeOffset.UtcNow }

            return! AuditRepository.insert dataSource scope entry
        }
