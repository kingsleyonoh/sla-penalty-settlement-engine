namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type NewAuditLog =
    { Id: Guid
      TenantId: Guid
      ActorKind: string
      ActorId: string
      Action: string
      EntityKind: string
      EntityId: Guid
      BeforeStateJson: string option
      AfterStateJson: string option
      OccurredAt: DateTimeOffset }

type AuditLogRow =
    { Id: Guid
      TenantId: Guid
      ActorKind: string
      ActorId: string
      Action: string
      EntityKind: string
      EntityId: Guid
      BeforeStateJson: string option
      AfterStateJson: string option
      OccurredAt: DateTimeOffset }

[<RequireQualifiedAccess>]
module AuditRepository =
    let private readAudit (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          ActorKind = reader.GetString(reader.GetOrdinal "actor_kind")
          ActorId = reader.GetString(reader.GetOrdinal "actor_id")
          Action = reader.GetString(reader.GetOrdinal "action")
          EntityKind = reader.GetString(reader.GetOrdinal "entity_kind")
          EntityId = reader.GetGuid(reader.GetOrdinal "entity_id")
          BeforeStateJson = Sql.stringOption reader "before_state_json"
          AfterStateJson = Sql.stringOption reader "after_state_json"
          OccurredAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal "occurred_at") }

    let insert (dataSource: NpgsqlDataSource) (scope: TenantScope) (entry: NewAuditLog) : Task<Guid> =
        task {
            if entry.TenantId <> TenantScope.value scope then
                invalidArg (nameof entry) "Audit entry tenant must match tenant scope."

            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into audit_log (
                        id,
                        tenant_id,
                        actor_kind,
                        actor_id,
                        action,
                        entity_kind,
                        entity_id,
                        before_state,
                        after_state,
                        occurred_at
                    )
                    values (
                        @id,
                        @tenant_id,
                        @actor_kind,
                        @actor_id,
                        @action,
                        @entity_kind,
                        @entity_id,
                        cast(@before_state as jsonb),
                        cast(@after_state as jsonb),
                        @occurred_at
                    )
                    """,
                    connection
                )

            Sql.addParameter command "id" entry.Id
            Sql.addParameter command "tenant_id" entry.TenantId
            Sql.addParameter command "actor_kind" entry.ActorKind
            Sql.addParameter command "actor_id" entry.ActorId
            Sql.addParameter command "action" entry.Action
            Sql.addParameter command "entity_kind" entry.EntityKind
            Sql.addParameter command "entity_id" entry.EntityId
            Sql.addOptionalParameter command "before_state" (entry.BeforeStateJson |> Option.map box)
            Sql.addOptionalParameter command "after_state" (entry.AfterStateJson |> Option.map box)
            Sql.addParameter command "occurred_at" entry.OccurredAt

            let! _ = command.ExecuteNonQueryAsync()
            return entry.Id
        }

    let listForEntity
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (entityKind: string)
        (entityId: Guid)
        : Task<AuditLogRow list> =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    select
                        id,
                        tenant_id,
                        actor_kind,
                        actor_id,
                        action,
                        entity_kind,
                        entity_id,
                        before_state::text as before_state_json,
                        after_state::text as after_state_json,
                        occurred_at
                    from audit_log
                    where tenant_id = @tenant_id
                      and entity_kind = @entity_kind
                      and entity_id = @entity_id
                    order by occurred_at desc
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "entity_kind" entityKind
            Sql.addParameter command "entity_id" entityId
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<AuditLogRow>()

            while reader.Read() do
                rows.Add(readAudit reader)

            return List.ofSeq rows
        }
