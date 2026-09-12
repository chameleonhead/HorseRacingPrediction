using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceListTabsComponentTests
{
    [TestMethod]
    public async Task CustomPeriod_UsesStandardFluentTabsAndOwnsThePageContent()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new RaceHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Races>();

        cut.WaitForAssertion(() => Assert.HasCount(1, cut.FindComponents<FluentTabs>()));
        var tabs = cut.FindComponent<FluentTabs>();
        Assert.IsTrue(tabs.Instance.ShowActiveIndicator);
        StringAssert.Contains(tabs.Instance.Class, "page-view-tabs");
        CollectionAssert.AreEqual(
            new[] { "今日", "今週", "結果確定", "すべて", "指定期間" },
            cut.FindComponents<FluentTab>().Select(x => x.Instance.Label?.ToString()).ToArray());
        StringAssert.Contains(cut.Find("#custom-panel").InnerHtml, "レース一覧");
    }

    [TestMethod]
    public async Task SelectingToday_UpdatesUrlAndSelectsTodayPanel()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new RaceHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => Assert.HasCount(1, cut.FindComponents<FluentTabs>()));

        await cut.InvokeAsync(() => cut.FindComponent<FluentTabs>().Instance.ActiveTabIdChanged.InvokeAsync("today"));

        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        cut.WaitForAssertion(() =>
        {
            var navigation = context.Services.GetRequiredService<NavigationManager>();
            StringAssert.Contains(navigation.Uri, $"from={today}");
            StringAssert.Contains(navigation.Uri, $"to={today}");
            StringAssert.Contains(cut.Find("#today-panel").InnerHtml, "レース一覧");
        });
    }

    private static BunitContext CreateContext(IServiceProvider services, HttpClient http)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(new AdminApiClient(http,
            new AdminApiBaseAddressResolver(services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { Key = TestApplicationFactory.TestApiKey })));
        return context;
    }

    private sealed class RaceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PagedResponse<RaceSummaryResponse>(
                [new("R001", DateOnly.FromDateTime(DateTime.Today), "06", 1, "テストレース",
                    HorseRacingPrediction.Contracts.RaceStatus.ResultDeclared, 16, "テストホース", DateTimeOffset.UtcNow)],
                1, 50, 1, 1))
            });
    }
}
