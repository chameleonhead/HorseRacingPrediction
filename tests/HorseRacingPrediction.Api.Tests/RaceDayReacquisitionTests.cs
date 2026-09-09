using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceDayReacquisitionTests
{
    [TestMethod]
    public async Task Requests_DeduplicateActiveDateAndAllowNewRequestAfterCompletion()
    {
        var database = Path.Combine(Path.GetTempPath(), "hrp-day-refresh-" + Guid.NewGuid().ToString("N") + ".db");
        var (app, client) = await TestApplicationFactory.CreateAsync("Data Source=" + database);
        await using var disposable = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var path = "/api/admin/race-days/reacquisition";

        var requests = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => http.PostAsJsonAsync(path, new { raceDate = date, reason = "馬主欠落の補完" })));
        foreach (var response in requests) Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var ids = await Task.WhenAll(requests.Select(x => x.Content.ReadFromJsonAsync<RequestResult>()));
        Assert.AreEqual(1, ids.Select(x => x!.JobId).Distinct().Count());

        var status = await http.GetFromJsonAsync<AgentJobDetailReadModel>($"{path}?raceDate={date:yyyy-MM-dd}");
        Assert.IsNotNull(status);
        Assert.AreEqual(AgentJobType.RaceDayReacquisition, status.JobType);
        Assert.AreEqual("馬主欠落の補完", status.AuditHistory.Single().Reason);

        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        await store.CompleteJobAsync(status.JobType, status.DeduplicationKey);
        var next = await http.PostAsJsonAsync(path, new { raceDate = date });
        Assert.AreNotEqual(status.JobId, (await next.Content.ReadFromJsonAsync<RequestResult>())!.JobId);
    }

    [TestMethod]
    public async Task Request_RequiresAuthenticationAndRejectsFutureDate()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var http = client;
        var path = "/api/admin/race-days/reacquisition";
        var body = new { raceDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)) };
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(path, body)).StatusCode);
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path, body)).StatusCode);
    }

    private sealed record RequestResult(string JobId);
}
