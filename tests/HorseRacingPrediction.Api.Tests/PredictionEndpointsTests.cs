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

    [TestMethod]
    public async Task AddBettingSuggestion_AfterCreate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-xyz", "AI", "model-v1", 0.8m, null),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/betting-suggestions",
            new AddBettingSuggestionRequest("WIN", "1", 1000m, null),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task AddPredictionRationale_AfterCreate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-xyz", "AI", "model-v1", 0.8m, null),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/rationales",
            new AddPredictionRationaleRequest("Horse", "entry-1", "SPEED", "120", "スピード指数が高い"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task FinalizeTicket_AfterCreate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-xyz", "AI", "model-v1", 0.8m, null),
            JsonOptions);

        var response = await _client.PostAsync(
            $"/api/predictions/{ticketId}/finalize", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task WithdrawTicket_AfterCreate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-xyz", "AI", "model-v1", 0.8m, null),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/withdraw",
            new WithdrawPredictionTicketRequest("予測精度が低いため取り下げ"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task CorrectPredictionMetadata_AfterCreate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-xyz", "AI", "model-v1", 0.8m, null),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CorrectPredictionMetadataRequest(0.75m, "修正後コメント", "信頼スコア誤り修正"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task EvaluatePredictionTicket_AfterCreate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-eval", "AI", "model-v1", 0.9m, null),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/evaluate",
            new EvaluatePredictionTicketRequest(
                "race-eval",
                DateTimeOffset.UtcNow,
                1,
                new[] { "WIN" },
                95.0m,
                1200m,
                1.2m),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RecalculatePredictionEvaluation_AfterEvaluate_ReturnsOk()
    {
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}",
            new CreatePredictionTicketRequest("race-recalc", "AI", "model-v1", 0.9m, null),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/evaluate",
            new EvaluatePredictionTicketRequest(
                "race-recalc",
                DateTimeOffset.UtcNow,
                1,
                new[] { "WIN" },
                null, null, null),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/recalculate-evaluation",
            new RecalculatePredictionEvaluationRequest(
                "race-recalc",
                DateTimeOffset.UtcNow,
                2,
                new[] { "WIN", "PLACE" },
                100m, 1500m, 1.5m),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
