using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class MemoEndpointsTests
{
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
    public async Task CreateMemo_ReturnsCreated()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var request = new CreateMemoRequest(
            AuthorId: "author-1",
            MemoType: "Note",
            Content: "テストメモ",
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects: new[] { new MemoSubjectDto("Horse", "horse-001") },
            MemoId: memoId);

        var response = await _client.PostAsJsonAsync("/api/memos", request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateMemo_MultipleSubjects_ReturnsCreated()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var request = new CreateMemoRequest(
            AuthorId: null,
            MemoType: "Observation",
            Content: "調教師×馬のメモ",
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects: new[]
            {
                new MemoSubjectDto("Horse", "horse-combo-1"),
                new MemoSubjectDto("Trainer", "trainer-combo-1")
            },
            MemoId: memoId);

        var response = await _client.PostAsJsonAsync("/api/memos", request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMemosBySubject_AfterCreate_ReturnsMemos()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var request = new CreateMemoRequest(
            AuthorId: "author-1",
            MemoType: "TrainingNote",
            Content: "調教コメント",
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects: new[] { new MemoSubjectDto("Horse", horseId) },
            MemoId: memoId);

        await _client.PostAsJsonAsync("/api/memos", request);

        var getResponse = await _client.GetAsync($"/api/memos/by-subject/Horse/{horseId}");
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

        var json = await getResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var memos = doc.RootElement.EnumerateArray().ToList();
        Assert.AreEqual(1, memos.Count);
        Assert.AreEqual(memoId, memos[0].GetProperty("memoId").GetString());
        Assert.AreEqual("TrainingNote", memos[0].GetProperty("memoType").GetString());
    }

    [TestMethod]
    public async Task GetMemosBySubject_WithUnknownSubjectType_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/memos/by-subject/Unknown/some-id");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateMemo_AfterCreate_ReturnsOk()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/memos", new CreateMemoRequest(
            null, "Note", "初期内容", DateTimeOffset.UtcNow,
            new[] { new MemoSubjectDto("Horse", "horse-update-1") },
            MemoId: memoId));

        var updateRequest = new UpdateMemoRequest(Content: "更新された内容");
        var response = await _client.PutAsJsonAsync($"/api/memos/{memoId}", updateRequest);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteMemo_AfterCreate_ReturnsOk()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/memos", new CreateMemoRequest(
            null, "Note", "削除対象", DateTimeOffset.UtcNow,
            new[] { new MemoSubjectDto("Jockey", "jockey-del-1") },
            MemoId: memoId));

        var response = await _client.DeleteAsync($"/api/memos/{memoId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ChangeMemoSubjects_AfterCreate_ReturnsOk()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var trainerId = $"trainer-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync("/api/memos", new CreateMemoRequest(
            null, "Note", "馬のメモ", DateTimeOffset.UtcNow,
            new[] { new MemoSubjectDto("Horse", horseId) },
            MemoId: memoId));

        var changeRequest = new ChangeMemoSubjectsRequest(new[]
        {
            new MemoSubjectDto("Horse", horseId),
            new MemoSubjectDto("Trainer", trainerId)
        });
        var response = await _client.PutAsJsonAsync($"/api/memos/{memoId}/subjects", changeRequest);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
