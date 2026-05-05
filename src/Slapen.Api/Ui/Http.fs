namespace Slapen.Api.Ui

open System
open Giraffe
open Microsoft.AspNetCore.Http
open Slapen.Api.Middleware

[<RequireQualifiedAccess>]
module UiHttp =
    let cookieName = "slapen_ui_api_key"

    let html title active body : HttpHandler =
        fun next ctx ->
            let tenant = TenantAuth.tryGetTenant ctx
            let content = Html.layout tenant active title body
            htmlString content next ctx

    let redirect path : HttpHandler = redirectTo false path

    let formValue (form: IFormCollection) name =
        match form.TryGetValue name with
        | true, value -> value.ToString()
        | _ -> ""

    let optionValue value =
        if String.IsNullOrWhiteSpace value then None else Some value

    let safeLocalPath value =
        if
            String.IsNullOrWhiteSpace value
            || value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        then
            "/"
        else
            value
