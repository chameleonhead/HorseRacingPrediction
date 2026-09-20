using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SubjectNameNormalizationEndpointsTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        (_app, _client) = await TestApplicationFactory.CreateAsync();
        _client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task Search_ReturnsCanonicalPreviewForSupportedSubjects()
    {
        var horseId = $"horse-{Guid.NewGuid():D}";
        var jockeyId = $"jockey-{Guid.NewGuid():D}";
        var trainerId = $"trainer-{Guid.NewGuid():D}";
        await RegisterHorseAsync(horseId, "マル外 テストホース", "マル外テストホース");
        await RegisterJockeyAsync(jockeyId, "▲ 山田 太郎（栗東）", "▲山田太郎（栗東）");
        await RegisterTrainerAsync(trainerId, "村山 明（栗東）", "むらやまあきら");

        var horse = await SearchAsync(ResourceType.Horse, horseId);
        var jockey = await SearchAsync(ResourceType.Jockey, jockeyId);
        var trainer = await SearchAsync(ResourceType.Trainer, trainerId);

        Assert.AreEqual("テストホース", horse.Items.Single().ProposedDisplayName);
        Assert.AreEqual("テストホース", horse.Items.Single().ProposedNormalizedName);
        Assert.AreEqual("山田 太郎", jockey.Items.Single().ProposedDisplayName);
        Assert.AreEqual("山田太郎", jockey.Items.Single().ProposedNormalizedName);
        Assert.AreEqual("村山 明", trainer.Items.Single().ProposedDisplayName);
        Assert.AreEqual("村山明", trainer.Items.Single().ProposedNormalizedName);
        Assert.IsTrue(trainer.Items.Single().CanApply);
    }

    [TestMethod]
    public async Task Search_BlocksCanonicalCollision()
    {
        var firstId = $"trainer-{Guid.NewGuid():D}";
        var secondId = $"trainer-{Guid.NewGuid():D}";
        await RegisterTrainerAsync(firstId, "衝突 太郎（栗東）", "collision-one");
        await RegisterTrainerAsync(secondId, "衝突 太郎（美浦）", "collision-two");

        var page = await SearchAsync(ResourceType.Trainer, "衝突 太郎");

        Assert.HasCount(2, page.Items);
        Assert.IsTrue(page.Items.All(x => !x.CanApply && x.Evaluation == "Conflict"));
        Assert.IsTrue(page.Items.All(x => x.ConflictingSubjectIds.Count == 1));
    }

    [TestMethod]
    public async Task Apply_CorrectsSelectedName_AndRepeatedRequestIsIdempotent()
    {
        var trainerId = $"trainer-{Guid.NewGuid():D}";
        await RegisterTrainerAsync(trainerId, "適用 花子（美浦）", "old-normalized");
        var candidate = (await SearchAsync(ResourceType.Trainer, trainerId)).Items.Single();
        var request = new ApplySubjectNameNormalizationRequest(
            [new(ResourceType.Trainer, trainerId, candidate.ManifestToken)]);

        var firstResponse = await _client.PostAsJsonAsync(
            "/api/admin/repairs/subject-name-normalization/apply", request);
        var first = await firstResponse.Content.ReadFromJsonAsync<SubjectNameNormalizationApplyResult>();
        var secondResponse = await _client.PostAsJsonAsync(
            "/api/admin/repairs/subject-name-normalization/apply", request);
        var second = await secondResponse.Content.ReadFromJsonAsync<SubjectNameNormalizationApplyResult>();
        var profile = await _client.GetFromJsonAsync<TrainerProfileResponse>($"/api/trainers/{trainerId}");

        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(1, first!.AppliedCount);
        Assert.AreEqual("適用 花子", profile!.DisplayName);
        Assert.AreEqual("適用花子", profile.NormalizedName);
        Assert.AreEqual(1, second!.SkippedCount);
        Assert.AreEqual("すでに正規化されています。", second.Items.Single().Message);
    }

    [TestMethod]
    public async Task Apply_SkipsWhenNameChangedAfterPreview()
    {
        var trainerId = $"trainer-{Guid.NewGuid():D}";
        await RegisterTrainerAsync(trainerId, "変更 前（栗東）", "before");
        var candidate = (await SearchAsync(ResourceType.Trainer, trainerId)).Items.Single();
        using var correction = await _client.PatchAsJsonAsync($"/api/trainers/{trainerId}",
            new CorrectTrainerDataRequest("変更 後（栗東）", "after", null, "並行更新"));
        correction.EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync(
            "/api/admin/repairs/subject-name-normalization/apply",
            new ApplySubjectNameNormalizationRequest(
                [new(ResourceType.Trainer, trainerId, candidate.ManifestToken)]));
        var result = await response.Content.ReadFromJsonAsync<SubjectNameNormalizationApplyResult>();

        Assert.AreEqual(1, result!.SkippedCount);
        StringAssert.Contains(result.Items.Single().Message, "検索後に名称が変更");
    }

    [TestMethod]
    public async Task Search_RejectsEmptyQueryAndUnsupportedOwner()
    {
        using var empty = await _client.GetAsync(
            "/api/admin/repairs/subject-name-normalization?subjectType=Trainer&query=");
        using var owner = await _client.GetAsync(
            "/api/admin/repairs/subject-name-normalization?subjectType=Owner&query=test");

        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, owner.StatusCode);
    }

    private async Task<SubjectNameNormalizationPage> SearchAsync(ResourceType type, string query) =>
        (await _client.GetFromJsonAsync<SubjectNameNormalizationPage>(
            $"/api/admin/repairs/subject-name-normalization?subjectType={type}&query={Uri.EscapeDataString(query)}"))!;

    private async Task RegisterHorseAsync(string id, string display, string normalized) =>
        (await _client.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(display, normalized, null, null, HorseId: id))).EnsureSuccessStatusCode();

    private async Task RegisterJockeyAsync(string id, string display, string normalized) =>
        (await _client.PostAsJsonAsync("/api/jockeys",
            new RegisterJockeyRequest(display, normalized, null, id))).EnsureSuccessStatusCode();

    private async Task RegisterTrainerAsync(string id, string display, string normalized) =>
        (await _client.PostAsJsonAsync("/api/trainers",
            new RegisterTrainerRequest(display, normalized, null, id))).EnsureSuccessStatusCode();
}
