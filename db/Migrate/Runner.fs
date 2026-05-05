namespace Slapen.DbMigrate

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open Npgsql

type RunnerOptions =
    { ConnectionString: string
      SeedFixtures: bool
      AutoSeed: bool }

type Migration =
    { Version: int
      Description: string
      Path: string
      Sql: string
      Checksum: string }

module Runner =
    let private command (connection: NpgsqlConnection) (transaction: NpgsqlTransaction option) sql =
        match transaction with
        | Some activeTransaction -> new NpgsqlCommand(sql, connection, activeTransaction)
        | None -> new NpgsqlCommand(sql, connection)

    let private execute (connection: NpgsqlConnection) transaction sql =
        use cmd = command connection transaction sql
        cmd.ExecuteNonQuery() |> ignore

    let private scalar<'T> (connection: NpgsqlConnection) transaction sql =
        use cmd = command connection transaction sql
        cmd.ExecuteScalar() :?> 'T

    let private checksum (content: string) =
        let bytes = Encoding.UTF8.GetBytes(content)
        let hash = SHA256.HashData(bytes)
        Convert.ToHexString(hash).ToLowerInvariant()

    let private repoRoot () =
        let rec walk (directory: DirectoryInfo) =
            let migrationsPath = Path.Combine(directory.FullName, "db", "migrations")

            if Directory.Exists migrationsPath then
                directory.FullName
            elif isNull directory.Parent then
                failwith "Could not find repository root containing db/migrations."
            else
                walk directory.Parent

        walk (DirectoryInfo(Directory.GetCurrentDirectory()))

    let private migrationFromPath (path: string) =
        let fileName = Path.GetFileNameWithoutExtension path
        let separator = fileName.IndexOf("__", StringComparison.Ordinal)

        if separator <> 3 then
            failwith $"Invalid migration filename '{Path.GetFileName path}'. Expected NNN__description.sql."

        let version = Int32.Parse(fileName.Substring(0, 3))
        let description = fileName.Substring(separator + 2).Replace("_", " ")
        let sql = File.ReadAllText path

        { Version = version
          Description = description
          Path = path
          Sql = sql
          Checksum = checksum sql }

    let private loadMigrations (root: string) =
        let migrationsDirectory = Path.Combine(root, "db", "migrations")

        Directory.GetFiles(migrationsDirectory, "*.sql")
        |> Array.map migrationFromPath
        |> Array.sortBy _.Version

    let private ensureMigrationTable (connection: NpgsqlConnection) =
        execute
            connection
            None
            """
            create table if not exists schema_migrations (
                version integer primary key,
                description text not null,
                checksum text not null,
                applied_at timestamptz not null default now()
            );
            """

    let private appliedMigrations (connection: NpgsqlConnection) =
        use cmd =
            command connection None "select version, checksum from schema_migrations order by version"

        use reader = cmd.ExecuteReader()
        let applied = Dictionary<int, string>()

        while reader.Read() do
            applied.Add(reader.GetInt32(0), reader.GetString(1))

        applied

    let private applyMigration
        (connection: NpgsqlConnection)
        (applied: Dictionary<int, string>)
        (migration: Migration)
        =
        if applied.ContainsKey migration.Version then
            if applied[migration.Version] <> migration.Checksum then
                let versionText = sprintf "%03i" migration.Version
                failwith $"Migration {versionText} checksum changed after it was applied."

            false
        else
            use transaction = connection.BeginTransaction()

            execute connection (Some transaction) migration.Sql

            use insert =
                new NpgsqlCommand(
                    """
                    insert into schema_migrations (version, description, checksum)
                    values (@version, @description, @checksum)
                    """,
                    connection,
                    transaction
                )

            insert.Parameters.AddWithValue("version", migration.Version) |> ignore
            insert.Parameters.AddWithValue("description", migration.Description) |> ignore
            insert.Parameters.AddWithValue("checksum", migration.Checksum) |> ignore
            insert.ExecuteNonQuery() |> ignore
            transaction.Commit()
            true

    let private applySeed (connection: NpgsqlConnection) (root: string) relativePath =
        let path = Path.Combine(root, relativePath)

        if File.Exists path then
            execute connection None (File.ReadAllText path)
        else
            failwith $"Seed file not found: {relativePath}"

    let private hasTenants (connection: NpgsqlConnection) =
        scalar<int64> connection None "select count(*) from tenants" > 0L

    let apply options =
        let root = repoRoot ()
        use connection = new NpgsqlConnection(options.ConnectionString)
        connection.Open()
        ensureMigrationTable connection

        let applied = appliedMigrations connection

        let appliedCount =
            loadMigrations root
            |> Array.sumBy (fun migration -> if applyMigration connection applied migration then 1 else 0)

        if options.AutoSeed then
            applySeed connection root (Path.Combine("db", "seeds", "signal_definitions.sql"))

            if options.SeedFixtures && not (hasTenants connection) then
                applySeed connection root (Path.Combine("db", "seeds", "fixtures", "tenants.sql"))
                applySeed connection root (Path.Combine("db", "seeds", "fixtures", "counterparties.sql"))
                applySeed connection root (Path.Combine("db", "seeds", "fixtures", "contracts_and_clauses.sql"))
                applySeed connection root (Path.Combine("db", "seeds", "fixtures", "breaches.sql"))

        appliedCount
