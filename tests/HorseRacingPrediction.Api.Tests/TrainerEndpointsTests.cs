using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class TrainerEndpointsTests
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
    public async Task RegisterTrainer_ReturnsCreated()
    {
        var trainerId = $"trainer-{Guid.NewGuid()}";
        var request = new RegisterTrainerRequest("池江泰寿", "ikejayasutoshi", "JRA", trainerId);

        var response = await _client.PostAsJsonAsync("/api/trainers", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task RegisterTrainer_ThenGetProfile_ReturnsCorrectData()
    {
        var trainerId = $"trainer-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/trainers",
            new RegisterTrainerRequest("国枝栄", "kunieda", "JRA", trainerId),
            JsonOptions);

        var response = await _client.GetAsync($"/api/trainers/{trainerId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<TrainerProfileResponse>(JsonOptions);
        Assert.IsNotNull(profile);
        Assert.AreEqual(trainerId, profile.TrainerId);
        Assert.AreEqual("国枝栄", profile.DisplayName);
        Assert.AreEqual("kunieda", profile.NormalizedName);
        Assert.AreEqual("JRA", profile.AffiliationCode);
    }

    [TestMethod]
    public async Task SearchTrainers_FiltersSortsAndPages()
    {
        var key = Guid.NewGuid().ToString("N");
        var trainerId1 = $"trainer-{Guid.NewGuid()}";
        var trainerId2 = $"trainer-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/trainers",
            new RegisterTrainerRequest($"SearchTrainerA-{key}", $"search-trainer-a-{key}", "JRA", trainerId1),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/trainers",
            new RegisterTrainerRequest($"SearchTrainerB-{key}", $"search-trainer-b-{key}", "JRA", trainerId2),
            JsonOptions);

        var response = await _client.GetAsync($"/api/trainers?query=SearchTrainer&affiliationCode=JRA&page=2&pageSize=1&sortBy=displayName&sortDescending=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<TrainerSummaryResponse>>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual(2, result.TotalPages);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(trainerId2, result.Items[0].TrainerId);
    }

    [TestMethod]
    public async Task UpdateTrainerProfile_AfterRegister_ReturnsOk()
    {
        var trainerId = $"trainer-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/trainers",
            new RegisterTrainerRequest("テスト調教師", "testtrainer", null, trainerId),
            JsonOptions);

        var response = await _client.PutAsJsonAsync(
            $"/api/trainers/{trainerId}",
            new UpdateTrainerProfileRequest(null, "testtrainer-updated", "JRA"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MergeTrainerAlias_AfterRegister_ReturnsOk()
    {
        var trainerId = $"trainer-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/trainers",
            new RegisterTrainerRequest("藤沢和雄", "fujisawakatsuo", "JRA", trainerId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/trainers/{trainerId}/aliases",
            new MergeAliasRequest("JRA", "T00456", "JRA-DATA", true),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task CorrectTrainerData_AfterRegister_ReturnsOk()
    {
        var trainerId = $"trainer-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/trainers",
            new RegisterTrainerRequest("テスト調教師", "testtrainer", null, trainerId),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/trainers/{trainerId}",
            new CorrectTrainerDataRequest(null, "testtrainer-fixed", "JRA", "名前誤り修正"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
