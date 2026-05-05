namespace Slapen.Data

open System
open System.Data.Common
open Npgsql

[<RequireQualifiedAccess>]
module Sql =
    let addParameter (command: NpgsqlCommand) (name: string) (value: obj) =
        command.Parameters.AddWithValue(name, value) |> ignore

    let addOptionalParameter (command: NpgsqlCommand) (name: string) (value: obj option) =
        let dbValue =
            match value with
            | Some actual -> actual
            | None -> DBNull.Value :> obj

        command.Parameters.AddWithValue(name, dbValue) |> ignore

    let stringOption (reader: DbDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetString ordinal)

    let guidOption (reader: DbDataReader) name =
        let ordinal = reader.GetOrdinal name

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetGuid ordinal)
