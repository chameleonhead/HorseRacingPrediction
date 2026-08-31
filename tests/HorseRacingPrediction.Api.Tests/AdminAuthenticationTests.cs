using System.Net;
using Microsoft.AspNetCore.TestHost;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class AdminAuthenticationTests
{
    private static WebApplication _app = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_app, _) = await TestApplicationFactory.CreateAsync();
    }

    [ClassCleanup]
    public static async Task ClassClean()
    {
        await _app.DisposeAsync();
    }

    private HttpClient CreateClient() => _app.GetTestClient();

    [TestMethod]
    public async Task GetLogin_IsAnonymous()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/login");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetRaces_WithoutCookie_RedirectsToLogin()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/races");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(response.Headers.Location!.ToString(), "/login");
    }

    [TestMethod]
    [DataRow("/owners")]
    [DataRow("/jobs")]
    [DataRow("/collection-tasks")]
    [DataRow("/acquisition-statuses")]
    [DataRow("/_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css")]
    public async Task AdminUiRoutes_WithoutApiKey_AreNotBlockedByApiKeyProtection(string path)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(path);

        Assert.AreNotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostLogin_WithWrongPassword_DoesNotIssueCookie()
    {
        using var client = CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "user",
            ["password"] = "wrong-key"
        });

        var response = await client.PostAsync("/login", form);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        StringAssert.Contains(response.Headers.Location!.ToString(), "/login");
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
    }

    [TestMethod]
    public async Task PostLogin_WithCorrectPassword_IssuesCookieAndAllowsAdminAccess()
    {
        using var client = CreateClient();
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "user",
            ["password"] = TestApplicationFactory.TestApiKey
        });

        var loginResponse = await client.PostAsync("/login", loginForm);

        Assert.AreEqual(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.AreEqual("/races", loginResponse.Headers.Location!.ToString());
        Assert.IsTrue(loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookieHeader = cookies!.First().Split(';')[0];

        using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        authenticatedRequest.Headers.Add("Cookie", cookieHeader);
        var adminResponse = await client.SendAsync(authenticatedRequest);

        Assert.AreEqual(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [TestMethod]
    public async Task PostLogin_WithLocalReturnUrl_RedirectsToRequestedPage()
    {
        using var client = CreateClient();
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "user",
            ["password"] = TestApplicationFactory.TestApiKey,
            ["ReturnUrl"] = "/owners?page=1"
        });

        var loginResponse = await client.PostAsync("/login", loginForm);

        Assert.AreEqual(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.AreEqual("/owners?page=1", loginResponse.Headers.Location!.ToString());
    }

    [TestMethod]
    public async Task PostLogin_WithRootReturnUrl_RedirectsToRaces()
    {
        using var client = CreateClient();
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "user",
            ["password"] = TestApplicationFactory.TestApiKey,
            ["ReturnUrl"] = "/"
        });

        var loginResponse = await client.PostAsync("/login", loginForm);

        Assert.AreEqual(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.AreEqual("/races", loginResponse.Headers.Location!.ToString());
    }

    [TestMethod]
    public async Task PostLogin_WithExternalReturnUrl_FallsBackToRaces()
    {
        using var client = CreateClient();
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "user",
            ["password"] = TestApplicationFactory.TestApiKey,
            ["ReturnUrl"] = "https://example.com/"
        });

        var loginResponse = await client.PostAsync("/login", loginForm);

        Assert.AreEqual(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.AreEqual("/races", loginResponse.Headers.Location!.ToString());
    }

    [TestMethod]
    public async Task PostLogout_ExpiresTheAuthCookie()
    {
        using var client = CreateClient();
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "user",
            ["password"] = TestApplicationFactory.TestApiKey
        });
        var loginResponse = await client.PostAsync("/login", loginForm);
        var cookieHeader = loginResponse.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/logout");
        logoutRequest.Headers.Add("Cookie", cookieHeader);
        var logoutResponse = await client.SendAsync(logoutRequest);

        Assert.AreEqual(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.IsTrue(logoutResponse.Headers.TryGetValues("Set-Cookie", out var logoutCookies));
        // サインアウトはブラウザに対しCookieの失効を指示する(サーバー側でチケットを無効化する仕組みは持たない)。
        StringAssert.Contains(logoutCookies!.First(), "expires=Thu, 01 Jan 1970");
    }

    [TestMethod]
    public async Task JsonApi_StillRequiresApiKey_IndependentOfCookieAuth()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/races/non-existent-race");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMlPrediction_RequiresApiKey()
    {
        // /api/races/{raceId}/ml-prediction もJSON APIの一部として /api 配下に置かれているため、
        // 管理UIの /races, /races/{id} とはパスが重ならず、常に保護対象である。
        using var client = CreateClient();

        var response = await client.GetAsync("/api/races/non-existent-race/ml-prediction");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
