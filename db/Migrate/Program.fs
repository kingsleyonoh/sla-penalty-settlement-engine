namespace Slapen.DbMigrate

open System

module Program =
    let private env name =
        let value = Environment.GetEnvironmentVariable name

        if String.IsNullOrWhiteSpace value then None else Some value

    let private parseArgs (args: string array) =
        let rec loop index connection seedFixtures =
            if index >= args.Length then
                connection, seedFixtures
            else
                match args[index] with
                | "--connection" when index + 1 < args.Length -> loop (index + 2) (Some args[index + 1]) seedFixtures
                | "--seed-fixtures" -> loop (index + 1) connection true
                | unknown -> failwith $"Unknown or incomplete argument: {unknown}"

        loop 0 None false

    [<EntryPoint>]
    let main args =
        try
            let connectionFromArgs, seedFixtures = parseArgs args

            let connectionString =
                connectionFromArgs
                |> Option.orElseWith (fun () -> env "DATABASE_URL")
                |> Option.defaultWith (fun () -> failwith "Missing --connection or DATABASE_URL.")

            let autoSeed =
                env "AUTO_SEED"
                |> Option.map (fun value -> not (value.Equals("false", StringComparison.OrdinalIgnoreCase)))
                |> Option.defaultValue true

            let applied =
                Runner.apply
                    { ConnectionString = connectionString
                      SeedFixtures = seedFixtures
                      AutoSeed = autoSeed }

            printfn "Applied %i migration(s)." applied
            0
        with error ->
            eprintfn "%s" error.Message
            1
