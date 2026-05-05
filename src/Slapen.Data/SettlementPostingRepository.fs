namespace Slapen.Data

open System
open System.Threading.Tasks
open Npgsql

[<RequireQualifiedAccess>]
module SettlementPostingRepository =
    let markPosting (connection: NpgsqlConnection) (transaction: NpgsqlTransaction) (scope: TenantScope) settlementId =
        task {
            use command =
                new NpgsqlCommand(
                    """
                    update settlements
                    set status = 'posting', posted_at = null, last_error = null
                    where tenant_id = @tenant_id and id = @id and status in ('ready', 'failed')
                    """,
                    connection,
                    transaction
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "id" settlementId
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }

    let markPostedLocal (dataSource: NpgsqlDataSource) scope settlementId pdfUrl (postedAt: DateTimeOffset) =
        task {
            use! connection = dataSource.OpenConnectionAsync().AsTask()

            use command =
                new NpgsqlCommand(
                    """
                    update settlements
                    set status = 'posted', pdf_url = @pdf_url, posted_at = @posted_at, last_error = null
                    where tenant_id = @tenant_id
                      and id = @id
                      and status in ('ready', 'posting', 'posted')
                    """,
                    connection
                )

            Sql.addParameter command "tenant_id" (TenantScope.value scope)
            Sql.addParameter command "id" settlementId
            Sql.addParameter command "pdf_url" pdfUrl
            Sql.addParameter command "posted_at" postedAt
            let! rows = command.ExecuteNonQueryAsync()
            return rows = 1
        }
