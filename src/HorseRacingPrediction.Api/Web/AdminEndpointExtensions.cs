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
        app.MapGet("/login", (string? error, string? returnUrl) =>
                Results.Content(BuildLoginHtml(error == "1", NormalizeReturnUrl(returnUrl)), "text/html; charset=utf-8"))
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
                return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(NormalizeReturnUrl(form["ReturnUrl"].ToString()))}");

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, AdminAuthenticationExtensions.AdminUserName) },
                CookieAuthenticationDefaults.AuthenticationScheme);

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity)).ConfigureAwait(false);

            return Results.Redirect(NormalizeReturnUrl(form["ReturnUrl"].ToString()));
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

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/races";

        if (string.Equals(returnUrl, "/", StringComparison.Ordinal))
            return "/races";

        return Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            && returnUrl.StartsWith("/", StringComparison.Ordinal)
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/races";
    }

    private static string BuildLoginHtml(bool showError, string returnUrl)
    {
        var errorHtml = showError
            ? "<p class=\"login-error\" role=\"alert\" aria-live=\"assertive\">ユーザー名またはパスワードが正しくありません。</p>"
            : string.Empty;

        return $$"""
            <!DOCTYPE html>
            <html lang="ja">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>ログイン - 競馬DB管理画面</title>
                <link rel="stylesheet" href="/_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css" />
                <link rel="stylesheet" href="/app.css" />
            </head>
            <body class="login-body">
                <main class="login-shell" aria-labelledby="login-title">
                    <section class="login-card">
                        <p class="login-eyebrow">Admin console</p>
                        <h1 id="login-title">競馬DB管理画面</h1>
                        <form method="post" action="/login" class="login-form">
                            <input type="hidden" name="ReturnUrl" value="{{System.Net.WebUtility.HtmlEncode(returnUrl)}}" />
                            {{errorHtml}}
                            <label class="login-field">
                                <span>ユーザー名</span>
                                <input class="login-input" name="username" value="user" autocomplete="username" required />
                            </label>
                            <label class="login-field">
                                <span>パスワード（APIキー）</span>
                                <input class="login-input" name="password" type="password" autocomplete="current-password" required autofocus />
                            </label>
                            <button class="login-button" type="submit">ログイン</button>
                        </form>
                    </section>
                </main>
            </body>
            </html>
            """;
    }
}
