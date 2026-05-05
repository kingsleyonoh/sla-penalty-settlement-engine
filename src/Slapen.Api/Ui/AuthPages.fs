namespace Slapen.Api.Ui

open Giraffe
open Microsoft.AspNetCore.Http
open Npgsql
open Slapen.Api.Middleware

[<RequireQualifiedAccess>]
module UiAuth =
    let login: HttpHandler =
        fun next ctx ->
            task {
                let returnUrl =
                    if ctx.Request.Query.ContainsKey "returnUrl" then
                        ctx.Request.Query["returnUrl"].ToString()
                    else
                        "/"

                let body =
                    $"""
                    <section class="login-shell">
                      <form class="login-panel" method="post" action="/login">
                        <h1>Ops console sign in</h1>
                        <p>Use an issued API key to open the tenant-scoped console.</p>
                        <input type="hidden" name="returnUrl" value="{Html.enc returnUrl}" />
                        <label for="apiKey">API key</label>
                        <input id="apiKey" name="apiKey" type="password" autocomplete="off" required />
                        <button class="primary" type="submit">Sign in</button>
                      </form>
                    </section>
                    """

                return! UiHttp.html "Login" "Login" body next ctx
            }

    let loginPost: HttpHandler =
        fun next ctx ->
            task {
                let dataSource = ctx.GetService<NpgsqlDataSource>()
                let! form = ctx.Request.ReadFormAsync()
                let apiKey = UiHttp.formValue form "apiKey"
                let returnUrl = UiHttp.safeLocalPath (UiHttp.formValue form "returnUrl")
                let! tenant = TenantAuth.resolveTenant dataSource apiKey

                match tenant with
                | None ->
                    let body =
                        """
                        <section class="login-shell">
                          <form class="login-panel" method="post" action="/login">
                            <h1>Ops console sign in</h1>
                            <p class="error">The API key was not accepted.</p>
                            <label for="apiKey">API key</label>
                            <input id="apiKey" name="apiKey" type="password" autocomplete="off" required />
                            <button class="primary" type="submit">Sign in</button>
                          </form>
                        </section>
                        """

                    return! UiHttp.html "Login" "Login" body next ctx
                | Some _ ->
                    let options =
                        CookieOptions(HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true)

                    ctx.Response.Cookies.Append(UiHttp.cookieName, apiKey, options)
                    return! UiHttp.redirect returnUrl next ctx
            }

    let logout: HttpHandler =
        fun next ctx ->
            ctx.Response.Cookies.Delete UiHttp.cookieName
            UiHttp.redirect "/login" next ctx
