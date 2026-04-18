using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class PredictionEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static WebApplication _app = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_app, _client) = await TestApplicationFactory.CreateAsync();
        _client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
    }

    [ClassCleanup]
    public static async Task ClassClean()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task CreatePredictionTicket_ReturnsCreated()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        var request = new CreatePredictionTicketRequest(
            "race-abc", "AI", "model-v1", 0.85m, "高確率予想");

        var response = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().Contains($"/api/predictions/{ticketId}"));
    }

    [TestMethod]
    public async Task GetPrediction_AfterCreate_ReturnsCorrectData()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-abc", "AI", "model-v1", 0.85m, "高確率予想"),
            JsonOptions);

        var response = await _client.GetAsync($"/api/predictions/{ticketId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<PredictionTicketResponse>(JsonOptions);
        Assert.IsNotNull(ticket);
        Assert.AreEqual(ticketId, ticket.PredictionTicketId);
        Assert.AreEqual("race-abc", ticket.RaceId);
        Assert.AreEqual("AI", ticket.PredictorType);
        Assert.AreEqual(0.85m, ticket.ConfidenceScore);
    }

    [TestMethod]
    public async Task AddMark_AfterCreate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-abc", "AI", "model-v1", 0.85m, null),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/marks",
            new AddPredictionMarkRequest("entry-1", "◎", 1, 90.5m, "本命"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task FullLifecycle_ProducesCorrectState()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-abc", "AI", "model-v1", 0.92m, "精密予測"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/marks",
            new AddPredictionMarkRequest("entry-1", "◎", 1, 95.0m, "本命"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/marks",
            new AddPredictionMarkRequest("entry-2", "○", 2, 80.0m, "対抗"),
            JsonOptions);

        var response = await _client.GetAsync($"/api/predictions/{ticketId}");
        var ticket = await response.Content.ReadFromJsonAsync<PredictionTicketResponse>(JsonOptions);

        Assert.IsNotNull(ticket);
        Assert.AreEqual("race-abc", ticket.RaceId);
        Assert.AreEqual(0.92m, ticket.ConfidenceScore);
        Assert.AreEqual(2, ticket.Marks.Count);
    }
}
