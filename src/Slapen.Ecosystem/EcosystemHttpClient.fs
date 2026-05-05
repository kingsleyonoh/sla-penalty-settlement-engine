namespace Slapen.Ecosystem

type EcosystemHttpClientConfig =
    { BaseUrl: string option
      ApiKey: string option
      Enabled: bool }

type EcosystemHttpClient =
    private
        { Config: EcosystemHttpClientConfig }

[<RequireQualifiedAccess>]
module EcosystemHttpClient =
    let create config = { Config = config }

    let isEnabled client =
        client.Config.Enabled
        && Option.isSome client.Config.BaseUrl
        && Option.isSome client.Config.ApiKey
