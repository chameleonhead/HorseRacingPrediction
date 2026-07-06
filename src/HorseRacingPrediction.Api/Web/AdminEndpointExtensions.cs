using System.Security.Claims;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Web;

public static class AdminEndpointExtensions
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/login", (string? error) =>
                Results.Content(BuildLoginHtml(error == "1"), "text/html; charset=utf-8"))
            .WithName("AdminLoginPage");

        app.MapPost("/login", async (HttpContext httpContext, IOptions<ApiKeyOptions> apiKeyOptions) =>
        {
            var form = await httpContext.Request.ReadFormAsync().ConfigureAwait(false);
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var expectedKey = apiKeyOptions.Value.Key;

            var isValid = string.Equals(username, AdminAuthenticationExtensions.AdminUserName, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(expectedKey)
                && string.Equals(password, expectedKey, StringComparison.Ordinal);

            if (!isValid)
                return Results.Redirect("/login?error=1");

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, AdminAuthenticationExtensions.AdminUserName) },
                CookieAuthenticationDefaults.AuthenticationScheme);

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity)).ConfigureAwait(false);

            return Results.Redirect("/");
        }).WithName("AdminLoginSubmit");

        app.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return Results.Redirect("/login");
        })
        .RequireAuthorization()
        .WithName("AdminLogout");

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .RequireAuthorization();

        return app;
    }

    private static string BuildLoginHtml(bool showError)
    {
        var errorHtml = showError
            ? "<p class=\"login-error\">ユーザー名またはパスワードが正しくありません。</p>"
            : string.Empty;

        return $$"""
            <!DOCTYPE html>
            <html lang="ja">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>ログイン - 競馬DB管理画面</title>
                <link rel="stylesheet" href="/app.css" />
            </head>
            <body class="login-body">
                <form method="post" action="/login" class="login-form">
                    <h1>競馬DB管理画面</h1>
                    {{errorHtml}}
                    <label>ユーザー名<input name="username" value="user" autocomplete="username" required /></label>
                    <label>パスワード（APIキー）<input name="password" type="password" autocomplete="current-password" required autofocus /></label>
                    <button class="btn primary" type="submit">ログイン</button>
                </form>
            </body>
            </html>
            """;
    }
}
