namespace Slapen.Setup

open System
open System.Security.Cryptography
open System.Text
open Microsoft.AspNetCore.Identity
open Npgsql
open Slapen.DbMigrate

module Program =
    let private env name =
        let value = Environment.GetEnvironmentVariable name

        if String.IsNullOrWhiteSpace value then None else Some value

    let private hash (value: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes value)
        |> Convert.ToHexString
        |> fun hashed -> hashed.ToLowerInvariant()

    let private randomHex byteCount =
        RandomNumberGenerator.GetBytes byteCount
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let private scalarInt64 (connection: NpgsqlConnection) sql =
        use command = new NpgsqlCommand(sql, connection)
        command.ExecuteScalar() :?> int64

    let private addTenantAndUser (connectionString: string) =
        let tenantId = Guid.NewGuid()
        let userId = Guid.NewGuid()
        let apiKey = "slapen_live_" + randomHex 32
        let password = randomHex 12
        let hasher = PasswordHasher<string>()
        let passwordHash = hasher.HashPassword("admin@default.local", password)

        use connection = new NpgsqlConnection(connectionString)
        connection.Open()
        use transaction = connection.BeginTransaction()

        use command =
            new NpgsqlCommand(
                """
                insert into tenants (
                    id,
                    name,
                    slug,
                    api_key_hash,
                    api_key_prefix,
                    legal_name,
                    full_legal_name,
                    display_name,
                    address,
                    registration,
                    contact,
                    locale,
                    timezone,
                    default_currency,
                    created_at,
                    updated_at
                )
                values (
                    @tenant_id,
                    'Default Tenant',
                    'default',
                    @api_key_hash,
                    @api_key_prefix,
                    'Default Tenant',
                    'Default Tenant',
                    'Default Tenant',
                    '{"line1":"Edit this address in settings","city":"Local","country":"XX"}',
                    '{}',
                    '{"email":"admin@default.local","ops_email":"admin@default.local"}',
                    'en-US',
                    'UTC',
                    'EUR',
                    @created_at,
                    @created_at
                );

                insert into users (
                    id,
                    tenant_id,
                    email,
                    password_hash,
                    display_name,
                    role,
                    is_active,
                    created_at,
                    updated_at
                )
                values (
                    @user_id,
                    @tenant_id,
                    'admin@default.local',
                    @password_hash,
                    'Default Supervisor',
                    'supervisor',
                    true,
                    @created_at,
                    @created_at
                );
                """,
                connection,
                transaction
            )

        command.Parameters.AddWithValue("tenant_id", tenantId) |> ignore
        command.Parameters.AddWithValue("user_id", userId) |> ignore
        command.Parameters.AddWithValue("api_key_hash", hash apiKey) |> ignore

        command.Parameters.AddWithValue("api_key_prefix", apiKey.Substring(0, 12))
        |> ignore

        command.Parameters.AddWithValue("password_hash", passwordHash) |> ignore
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow) |> ignore
        command.ExecuteNonQuery() |> ignore
        transaction.Commit()
        apiKey, password

    [<EntryPoint>]
    let main _ =
        try
            let connectionString =
                env "DATABASE_URL"
                |> Option.defaultWith (fun () -> failwith "DATABASE_URL is required.")

            Runner.apply
                { ConnectionString = connectionString
                  SeedFixtures = false
                  AutoSeed = true }
            |> ignore

            use connection = new NpgsqlConnection(connectionString)
            connection.Open()

            if scalarInt64 connection "select count(*) from tenants" > 0L then
                printfn "Already initialized."
                0
            else
                let apiKey, password = addTenantAndUser connectionString
                printfn "First-run setup complete."
                printfn "API Key: %s" apiKey
                printfn "Supervisor: admin@default.local  Password: %s" password
                printfn "Open: https://localhost:5109"
                0
        with error ->
            eprintfn "%s" error.Message
            1
