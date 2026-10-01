using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.ApiClient.Races;
using HorseRacingPrediction.Contracts.Races;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using System.Net;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class ApiClientTestServerIntegrationTests
{
    [TestMethod]
    public async Task RefitFacade_RoundTripsThroughExistingApiTestServer()
    {
        var (app, _) = await TestApplicationFactory.CreateAsync();
        await using var appLifetime = app;

        Assert.IsNull(app.Services.GetService<IApiClientFactory>());
        Assert.IsNull(app.Services.GetService<IRacesApi>());

        var services = new ServiceCollection();
        var responseCapture = new ApiClientTestResponseCaptureHandler();
        var builder = services.AddHorseRacingApiClient(options =>
        {
            options.BaseAddress = new Uri("http://localhost/");
            options.ApiKey = TestApplicationFactory.TestApiKey;
        });
        builder.AddHttpMessageHandler(() => responseCapture);
        builder.ConfigurePrimaryHttpMessageHandler(() => app.GetTestServer().CreateHandler());
        using var provider = services.BuildServiceProvider();

        var races = provider.GetRequiredService<IApiClientFactory>().Create<IRacesApi>();
        var raceId = $"race-{Guid.NewGuid():D}";
        var raceDate = new DateOnly(2026, 9, 27);
        var created = await races.CreateRaceAsync(new CreateRaceRequest(new CreateRaceInputDto(
            raceDate, "TOKYO", 7, "Refit TestServer Target", raceId)));
        using (created)
        {
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode,
                $"Refit error: {created.Error}; TestServer status/body: {responseCapture.LastStatusCode} {responseCapture.LastResponseBody}");
            Assert.AreEqual(raceId, created.Content?.RaceId);
        }

        var otherRace = await races.CreateRaceAsync(new CreateRaceRequest(new CreateRaceInputDto(
            new DateOnly(2026, 9, 28), "TOKYO", 8, "Refit TestServer Other", $"race-{Guid.NewGuid():D}")));
        using (otherRace)
            Assert.AreEqual(HttpStatusCode.Created, otherRace.StatusCode);

        using var found = await races.GetRaceAsync(new GetRaceRequest { RaceId = raceId });
        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
        Assert.AreEqual(raceId, found.Content?.Race.RaceId);
        Assert.AreEqual("Refit TestServer Target", found.Content?.Race.RaceName);

        using var search = await races.SearchRacesAsync(new SearchRacesRequest
        {
            RaceDateFrom = raceDate,
            RaceDateTo = raceDate,
            RaceName = "Refit TestServer Target",
            Page = 1,
            PageSize = 10
        });
        Assert.AreEqual(HttpStatusCode.OK, search.StatusCode);
        Assert.IsNotNull(search.Content);
        Assert.AreEqual(1, search.Content.Races.Count);
        Assert.AreEqual(raceId, search.Content.Races[0].RaceId);
        Assert.AreEqual(1, search.Content.Pagination.TotalCount);

        var observation = new RecordTrackConditionRequest(new RecordTrackConditionInputDto(
            new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.FromHours(9)), "G", "D", "good"))
        {
            RaceId = raceId
        };
        using var recorded = await races.RecordTrackConditionAsync(observation);
        Assert.AreEqual(HttpStatusCode.OK, recorded.StatusCode);
        Assert.IsNull(recorded.Error);
        Assert.AreEqual(string.Empty, responseCapture.LastResponseBody);

        using var invalidSearch = await races.SearchRacesAsync(new SearchRacesRequest { Page = 0 });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidSearch.StatusCode);
        Assert.IsInstanceOfType<ApiException>(invalidSearch.Error);
        Assert.AreEqual("[\"Page must be greater than or equal to 1.\"]", ((ApiException)invalidSearch.Error!).Content);
    }
}
