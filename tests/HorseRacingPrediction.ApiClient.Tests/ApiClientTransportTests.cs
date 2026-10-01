using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.ApiClient.Collection;
using HorseRacingPrediction.ApiClient.Races;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Races;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Refit;
using System.Net;
using System.Text.Json;

namespace HorseRacingPrediction.ApiClient.Tests;

[TestClass]
public sealed class ApiClientTransportTests
{
    [TestMethod]
    public async Task SearchRaces_SplitsPathAndDateQuery_UsesIsoDateNumericEnumAndNoBody()
    {
        var handler = new ApiClientTestCapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            "{\"races\":[],\"pagination\":{}}"));
        using var services = CreateServices(handler);
        var request = new SearchRacesRequest
        {
            RaceDateFrom = new DateOnly(2026, 5, 1),
            RaceDateTo = new DateOnly(2026, 5, 31),
            Status = RaceStatus.PreRaceOpen,
            Page = 2
        };

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<IRacesApi>().SearchRacesAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var captured = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, captured.Method);
        Assert.AreEqual("/api/races", captured.RequestUri!.AbsolutePath);
        Assert.IsNull(captured.Body);
        var query = Uri.UnescapeDataString(captured.RequestUri.Query);
        StringAssert.Contains(query, "raceDateFrom=2026-05-01");
        StringAssert.Contains(query, "raceDateTo=2026-05-31");
        StringAssert.Contains(query, "status=2");
        StringAssert.Contains(query, "page=2");
        Assert.IsFalse(query.Contains("raceId=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task CollectionList_FormatsDateTimeOffsetQueryAndOmitsNulls()
    {
        var handler = new ApiClientTestCapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            "{\"tasks\":[],\"pagination\":{}}"));
        using var services = CreateServices(handler);
        var from = new DateTimeOffset(2026, 5, 1, 9, 2, 3, TimeSpan.FromHours(9));

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<ICollectionApi>().ListCollectionTasksAsync(new ListCollectionTasksRequest(CreatedFrom: from));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var captured = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, captured.Method);
        Assert.IsNull(captured.Body);
        var query = Uri.UnescapeDataString(captured.RequestUri!.Query);
        StringAssert.Contains(query, "createdFrom=2026-05-01T09:02:03.0000000+09:00");
        Assert.IsFalse(query.Contains("createdTo=", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("status=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RegisterEntry_WrapsBodyAndWritesEnumNumerically_LeavesRouteIdOutOfJson()
    {
        var handler = new ApiClientTestCapturingHandler(_ => JsonResponse(HttpStatusCode.Created, "{}", "/api/races/race-99/entries/entry-7"));
        using var services = CreateServices(handler);
        var request = new RegisterEntryRequest(new RegisterEntryInputDto(
            HorseId: "horse-4", HorseNumber: 3, JockeyId: "jockey-5", TrainerId: "trainer-6",
            GateNumber: 7, AssignedWeight: 56.5m, SexCode: "M", Age: 4, DeclaredWeight: 480,
            DeclaredWeightDiff: -2, ParticipationStatus: RaceEntryParticipationStatus.Cancelled))
        {
            RaceId = "race-99"
        };

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<IRacesApi>().RegisterEntryAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual("/api/races/race-99/entries/entry-7", response.Headers?.Location?.OriginalString);
        var captured = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, captured.Method);
        Assert.AreEqual("/api/races/race-99/entries", captured.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(captured.Body!);
        var root = body.RootElement;
        Assert.AreEqual(1, root.EnumerateObject().Count());
        var entry = root.GetProperty("entry");
        Assert.AreEqual("horse-4", entry.GetProperty("horseId").GetString());
        Assert.AreEqual(3, entry.GetProperty("horseNumber").GetInt32());
        Assert.AreEqual(7, entry.GetProperty("gateNumber").GetInt32());
        Assert.AreEqual(1, entry.GetProperty("participationStatus").GetInt32());
        Assert.IsFalse(root.TryGetProperty("raceId", out _));
    }

    [TestMethod]
    [DataRow("\"PreRaceOpen\"", RaceStatus.PreRaceOpen)]
    [DataRow("2", RaceStatus.PreRaceOpen)]
    public async Task GetRace_ReadsStringAndNumericEnumResponses(string status, RaceStatus expected)
    {
        var json = $"{{\"race\":{{\"raceId\":\"race-42\",\"status\":{status},\"resultDeclaredAt\":\"2026-05-03T12:00:00+09:00\",\"entries\":[],\"weatherObservations\":[],\"trackConditionObservations\":[],\"entryResults\":[]}}}}";
        var handler = new ApiClientTestCapturingHandler(_ => JsonResponse(HttpStatusCode.OK, json));
        using var services = CreateServices(handler);

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<IRacesApi>().GetRaceAsync(new GetRaceRequest { RaceId = "race-42" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("race-42", response.Content?.Race.RaceId);
        Assert.AreEqual(expected, response.Content?.Race.Status);
        Assert.AreEqual(TimeSpan.FromHours(9), response.Content?.Race.ResultDeclaredAt?.Offset);
        Assert.AreEqual("/api/races/race-42", handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.IsNull(handler.Requests.Single().Body);
    }

    [TestMethod]
    public async Task NoDataOperation_Preserves204AndRawApiErrorBodies()
    {
        var handler = new ApiClientTestCapturingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/close", StringComparison.Ordinal)
            ? new ApiClientTestCapturedResponse(HttpStatusCode.NoContent, string.Empty)
            : new ApiClientTestCapturedResponse(HttpStatusCode.Conflict, "{\"code\":\"RaceConflict\",\"message\":\"busy\"}"));
        using var services = CreateServices(handler);
        var api = services.GetRequiredService<IApiClientFactory>().Create<IRacesApi>();

        using var closed = await api.CloseRaceLifecycleAsync(new CloseRaceLifecycleRequest { RaceId = "race-42" });
        Assert.AreEqual(HttpStatusCode.NoContent, closed.StatusCode);
        var closeRequest = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, closeRequest.Method);
        Assert.IsNull(closeRequest.Body);

        using var conflict = await api.GetRaceAsync(new GetRaceRequest { RaceId = "race-locked" });
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.IsNull(conflict.Content);
        var apiError = conflict.Error as ApiException;
        Assert.IsNotNull(apiError);
        Assert.AreEqual(HttpStatusCode.Conflict, apiError.StatusCode);
        Assert.AreEqual("{\"code\":\"RaceConflict\",\"message\":\"busy\"}", apiError.Content);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, "{\"code\":\"Invalid\"}")]
    [DataRow(HttpStatusCode.NotFound, "{\"code\":\"Missing\"}")]
    [DataRow(HttpStatusCode.Conflict, "{\"code\":\"Locked\"}")]
    public async Task DataOperation_RetainsNonSuccessStatusAndRawBody(HttpStatusCode status, string errorBody)
    {
        var handler = new ApiClientTestCapturingHandler(_ => JsonResponse(status, errorBody));
        using var services = CreateServices(handler);

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<IRacesApi>().GetRaceAsync(new GetRaceRequest { RaceId = "race-err" });

        Assert.AreEqual(status, response.StatusCode);
        Assert.IsNull(response.Content);
        var apiError = response.Error as ApiException;
        Assert.IsNotNull(apiError);
        Assert.AreEqual(errorBody, apiError.Content);
    }

    [TestMethod]
    [DataRow("{\"acquisition\":{\"status\":\"NoWork\",\"noWorkReason\":\"PipelinePaused\",\"startBefore\":\"2026-05-03T12:00:00-04:00\",\"safeToReleaseReservation\":true}}", -4)]
    [DataRow("{\"acquisition\":{\"status\":\"NoWork\",\"startBefore\":null}}", 99)]
    [DataRow("{\"acquisition\":{\"status\":\"NoWork\"}}", 99)]
    public async Task AcquireExecution_DeserializesStringEnumOffsetNullAndComputedFields(string json, int expectedOffsetHours)
    {
        var handler = new ApiClientTestCapturingHandler(_ => JsonResponse(HttpStatusCode.OK, json));
        using var services = CreateServices(handler);

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<ICollectionApi>().AcquireNextExecutionAsync(new AcquireNextExecutionRequest(null));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var acquisition = response.Content?.Acquisition;
        Assert.IsNotNull(acquisition);
        Assert.AreEqual(CollectionExecutionAcquireStatus.NoWork, acquisition.Status);
        if (expectedOffsetHours == 99)
            Assert.IsNull(acquisition.StartBefore);
        else
            Assert.AreEqual(TimeSpan.FromHours(expectedOffsetHours), acquisition.StartBefore?.Offset);
        Assert.AreEqual(expectedOffsetHours == -4, acquisition.SafeToReleaseReservation);
    }

    [TestMethod]
    public async Task CancellationToken_ReachesHttpTransport()
    {
        var handler = new ApiClientTestCapturingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ApiClientTestCapturedResponse(HttpStatusCode.OK, "{}");
        });
        using var services = CreateServices(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () =>
            await services.GetRequiredService<IApiClientFactory>().Create<ICollectionApi>()
                .GetCollectionPipelineAsync(cancellation.Token));
        Assert.IsTrue(handler.ObservedCancellation);
    }

    private static ServiceProvider CreateServices(ApiClientTestCapturingHandler handler)
    {
        var services = new ServiceCollection();
        var builder = services.AddHorseRacingApiClient(options =>
        {
            options.BaseAddress = new Uri("https://api.example.test/");
            options.ApiKey = null;
            options.Timeout = Timeout.InfiniteTimeSpan;
        });
        builder.ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static ApiClientTestCapturedResponse JsonResponse(HttpStatusCode status, string json, string? location = null)
        => new(status, json, location);
}
