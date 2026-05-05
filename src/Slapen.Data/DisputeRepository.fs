namespace Slapen.Data

open System
open System.Threading.Tasks
open Npgsql
open Slapen.Domain

[<RequireQualifiedAccess>]
module DisputeRepository =
    let openDispute
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        (reason: string)
        (userId: Guid option)
        (disputedAt: DateTimeOffset)
        =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update breach_events
                    set status = 'disputed',
                        disputed_reason = @reason,
                        disputed_at = @disputed_at,
                        disputed_by_user_id = @user_id,
                        updated_at = @disputed_at
                    where tenant_id = @tenant_id
                      and id = @breach_id
                      and status = 'accrued'
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "breach_id" breachEventId
            Sql.addParameter command "reason" reason
            Sql.addOptionalParameter command "user_id" (userId |> Option.map box)
            Sql.addParameter command "disputed_at" disputedAt
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }

    let resolveInOurFavor
        (dataSource: NpgsqlDataSource)
        (scope: TenantScope)
        (breachEventId: Guid)
        (resolvedAt: DateTimeOffset)
        =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update breach_events
                    set status = 'accrued',
                        updated_at = @resolved_at
                    where tenant_id = @tenant_id
                      and id = @breach_id
                      and status = 'disputed'
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "breach_id" breachEventId
            Sql.addParameter command "resolved_at" resolvedAt
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }

    let status (dataSource: NpgsqlDataSource) (scope: TenantScope) (breachEventId: Guid) =
        task {
            let! row = BreachEventRowsRepository.findById dataSource scope breachEventId

            return
                row
                |> Option.map (fun breach -> DomainMapping.breachStatusFromText breach.Status)
        }
