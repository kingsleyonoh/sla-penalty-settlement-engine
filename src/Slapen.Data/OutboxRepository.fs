namespace Slapen.Data

open System
open System.Data.Common
open System.Threading.Tasks
open Npgsql

type NewOutboxMessage =
    { Id: Guid
      Operation: string
      PayloadJson: string
      IdempotencyKey: string option
      NextRunAt: DateTimeOffset }

type OutboxMessage =
    { Id: Guid
      TenantId: Guid
      Operation: string
      PayloadJson: string
      Attempts: int
      IdempotencyKey: string option }

[<RequireQualifiedAccess>]
module OutboxRepository =
    let private readMessage (reader: DbDataReader) =
        { Id = reader.GetGuid(reader.GetOrdinal "id")
          TenantId = reader.GetGuid(reader.GetOrdinal "tenant_id")
          Operation = reader.GetString(reader.GetOrdinal "op")
          PayloadJson = reader.GetString(reader.GetOrdinal "payload")
          Attempts = reader.GetInt32(reader.GetOrdinal "attempts")
          IdempotencyKey = Sql.stringOption reader "idempotency_key" }

    let enqueue (dataSource: NpgsqlDataSource) (scope: TenantScope) (message: NewOutboxMessage) =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    insert into outbox (
                        id, tenant_id, op, payload, status, attempts, next_run_at,
                        idempotency_key, created_at, updated_at
                    )
                    values (
                        @id, @tenant_id, @op, cast(@payload as jsonb), 'pending', 0,
                        @next_run_at, @idempotency_key, @now, @now
                    )
                    on conflict (tenant_id, idempotency_key)
                    where idempotency_key is not null
                    do update set updated_at = outbox.updated_at
                    returning id
                    """,
                    connection
                )

            Sql.addParameter command "id" message.Id
            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "op" message.Operation
            Sql.addParameter command "payload" message.PayloadJson
            Sql.addParameter command "next_run_at" message.NextRunAt
            Sql.addOptionalParameter command "idempotency_key" (message.IdempotencyKey |> Option.map box)
            Sql.addParameter command "now" DateTimeOffset.UtcNow
            let! id = command.ExecuteScalarAsync()
            return id :?> Guid
        }

    let enqueueWithinTransaction
        (connection: NpgsqlConnection)
        (transaction: NpgsqlTransaction)
        (scope: TenantScope)
        (message: NewOutboxMessage)
        =
        task {
            use command =
                new NpgsqlCommand(
                    """
                    insert into outbox (
                        id, tenant_id, op, payload, status, attempts, next_run_at,
                        idempotency_key, created_at, updated_at
                    )
                    values (
                        @id, @tenant_id, @op, cast(@payload as jsonb), 'pending', 0,
                        @next_run_at, @idempotency_key, @now, @now
                    )
                    on conflict (tenant_id, idempotency_key)
                    where idempotency_key is not null
                    do update set updated_at = outbox.updated_at
                    returning id
                    """,
                    connection,
                    transaction
                )

            Sql.addParameter command "id" message.Id
            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "op" message.Operation
            Sql.addParameter command "payload" message.PayloadJson
            Sql.addParameter command "next_run_at" message.NextRunAt
            Sql.addOptionalParameter command "idempotency_key" (message.IdempotencyKey |> Option.map box)
            Sql.addParameter command "now" DateTimeOffset.UtcNow
            let! id = command.ExecuteScalarAsync()
            return id :?> Guid
        }

    let leaseDue
        (dataSource: NpgsqlDataSource)
        (batchSize: int)
        (leaseOwner: string)
        (now: DateTimeOffset)
        (lockedUntil: DateTimeOffset)
        =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update outbox
                    set status = 'in_flight',
                        locked_by = @locked_by,
                        locked_until = @locked_until,
                        updated_at = @now
                    where id in (
                        select id
                        from outbox
                        where status in ('pending', 'failed')
                          and next_run_at <= @now
                        order by next_run_at, created_at
                        limit @batch_size
                        for update skip locked
                    )
                    returning id, tenant_id, op, payload::text as payload, attempts, idempotency_key
                    """,
                    connection
                )

            Sql.addParameter command "locked_by" leaseOwner
            Sql.addParameter command "locked_until" lockedUntil
            Sql.addParameter command "now" now
            Sql.addParameter command "batch_size" batchSize
            use! reader = command.ExecuteReaderAsync()
            let rows = ResizeArray<OutboxMessage>()

            while reader.Read() do
                rows.Add(readMessage reader)

            return List.ofSeq rows
        }

    let markDone (dataSource: NpgsqlDataSource) (messageId: Guid) (now: DateTimeOffset) =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update outbox
                    set status = 'done',
                        attempts = attempts + 1,
                        last_error = null,
                        locked_by = null,
                        locked_until = null,
                        updated_at = @now
                    where id = @id and status = 'in_flight'
                    """,
                    connection
                )

            Sql.addParameter command "id" messageId
            Sql.addParameter command "now" now
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }

    let markFailed
        (dataSource: NpgsqlDataSource)
        (messageId: Guid)
        (maxAttempts: int)
        (error: string)
        (nextRunAt: DateTimeOffset)
        (now: DateTimeOffset)
        =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update outbox
                    set status = case when attempts + 1 >= @max_attempts then 'dead' else 'failed' end,
                        attempts = attempts + 1,
                        last_error = @last_error,
                        next_run_at = @next_run_at,
                        locked_by = null,
                        locked_until = null,
                        updated_at = @now
                    where id = @id and status = 'in_flight'
                    returning status
                    """,
                    connection
                )

            Sql.addParameter command "id" messageId
            Sql.addParameter command "max_attempts" maxAttempts
            Sql.addParameter command "last_error" error
            Sql.addParameter command "next_run_at" nextRunAt
            Sql.addParameter command "now" now
            let! value = command.ExecuteScalarAsync()
            return value :?> string
        }
