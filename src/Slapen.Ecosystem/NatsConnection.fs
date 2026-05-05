namespace Slapen.Ecosystem

open System.Threading.Tasks

type NatsConnectionConfig =
    { Url: string
      CredentialsPath: string option
      Enabled: bool }

type NatsConnection =
    private { Config: NatsConnectionConfig }

[<RequireQualifiedAccess>]
module NatsConnection =
    type ConnectionResult =
        | Disabled
        | ConnectedStub

    let create config = { Config = config }

    let connect connection : Task<ConnectionResult> =
        task {
            if connection.Config.Enabled then
                return ConnectedStub
            else
                return Disabled
        }
