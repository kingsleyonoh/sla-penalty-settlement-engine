namespace Slapen.Api.Handlers

open System
open Giraffe
open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module Response =
    let notFound: HttpHandler =
        setStatusCode StatusCodes.Status404NotFound >=> json {| error = "not_found" |}

    let badRequest message : HttpHandler =
        setStatusCode StatusCodes.Status400BadRequest
        >=> json
                {| error = "bad_request"
                   message = message |}

    let conflict message : HttpHandler =
        setStatusCode StatusCodes.Status409Conflict
        >=> json
                {| error = "conflict"
                   message = message |}

    let created location body : HttpHandler =
        setStatusCode StatusCodes.Status201Created
        >=> setHttpHeader "Location" location
        >=> json body

    let clampLimit value = Math.Clamp(value, 1, 100)
