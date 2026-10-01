using System.Net;
using System.Net.Http.Json;

using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Contracts.Memos;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        var request = new CreateMemoRequest(new CreateMemoInputDto(
            AuthorId: "author-1",
            MemoType: "Note",
            Content: "テストメモ",
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects: new[] { new MemoSubjectDto("Horse", "horse-001") },
            MemoId: memoId));

        var response = await _client.PostAsJsonAsync("/api/memos", request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().EndsWith($"/api/memos/{memoId}", StringComparison.Ordinal));
        var receipt = await response.Content.ReadFromJsonAsync<CreateMemoResponse>();
        Assert.IsNotNull(receipt);
        Assert.AreEqual(memoId, receipt.MemoId);
    }

    [TestMethod]
    public async Task CreateMemo_MultipleSubjects_ReturnsCreated()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var request = new CreateMemoRequest(new CreateMemoInputDto(
            AuthorId: null,
            MemoType: "Observation",
            Content: "調教師×馬のメモ",
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects: new[]
            {
                new MemoSubjectDto("Horse", "horse-combo-1"),
                new MemoSubjectDto("Trainer", "trainer-combo-1")
            },
            MemoId: memoId));

        var response = await _client.PostAsJsonAsync("/api/memos", request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateMemo_DuplicateMemoId_ReturnsConflict()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var request = new CreateMemoRequest(new CreateMemoInputDto(
            AuthorId: "author-1",
            MemoType: "SnsStoryPost",
            Content: "初回の投稿文",
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects: new[] { new MemoSubjectDto("Race", "race-dup-1") },
            MemoId: memoId));

        var firstResponse = await _client.PostAsJsonAsync("/api/memos", request);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync("/api/memos", request);
        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [TestMethod]
    public async Task GetMemosBySubject_AfterCreate_ReturnsMemos()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var request = new CreateMemoRequest(new CreateMemoInputDto(
            AuthorId: "author-1",
            MemoType: "TrainingNote",
            Content: "調教コメント",
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects: new[] { new MemoSubjectDto("Horse", horseId) },
            MemoId: memoId));

        await _client.PostAsJsonAsync("/api/memos", request);

        var getResponse = await _client.GetAsync($"/api/memos/by-subject/Horse/{horseId}");
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

        var response = await getResponse.Content.ReadFromJsonAsync<GetMemosBySubjectResponse>();
        Assert.IsNotNull(response);
        Assert.HasCount(1, response.Memos);
        Assert.AreEqual(memoId, response.Memos[0].MemoId);
        Assert.AreEqual("TrainingNote", response.Memos[0].MemoType);
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
        await _client.PostAsJsonAsync("/api/memos", new CreateMemoRequest(new(
            null, "Note", "初期内容", DateTimeOffset.UtcNow,
            new[] { new MemoSubjectDto("Horse", "horse-update-1") },
            MemoId: memoId)));

        var updateRequest = new UpdateMemoRequest(memoId, new(Content: "更新された内容"));
        var response = await _client.PutAsJsonAsync($"/api/memos/{memoId}", updateRequest);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteMemo_AfterCreate_ReturnsOk()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/memos", new CreateMemoRequest(new(
            null, "Note", "削除対象", DateTimeOffset.UtcNow,
            new[] { new MemoSubjectDto("Jockey", "jockey-del-1") },
            MemoId: memoId)));

        var response = await _client.DeleteAsync($"/api/memos/{memoId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ChangeMemoSubjects_AfterCreate_ReturnsOk()
    {
        var memoId = $"memo-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var trainerId = $"trainer-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync("/api/memos", new CreateMemoRequest(new(
            null, "Note", "馬のメモ", DateTimeOffset.UtcNow,
            new[] { new MemoSubjectDto("Horse", horseId) },
            MemoId: memoId)));

        var changeRequest = new ChangeMemoSubjectsRequest(memoId, new[]
        {
            new MemoSubjectDto("Horse", horseId),
            new MemoSubjectDto("Trainer", trainerId)
        });
        var response = await _client.PutAsJsonAsync($"/api/memos/{memoId}/subjects", changeRequest);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RaceWriteFilter_WaitsOnNestedOpaqueMemoIdLock_BeforeInvokingEndpoint()
    {
        var coordinator = _app.Services.GetRequiredService<RaceWriteCoordinator>();
        var held = await coordinator.AcquireAsync(["memo-id:notes-1"], CancellationToken.None);
        try
        {
            var request = new CreateMemoRequest(new CreateMemoInputDto(
                "author-1", "Note", "lock boundary probe", DateTimeOffset.UtcNow,
                [new MemoSubjectDto("Horse", "horse-probe")], MemoId: "notes-1"));
            var filter = _app.Services.GetRequiredService<RaceWriteEndpointFilter>();
            using var cancellation = new CancellationTokenSource();
            var blockedContext = CreateMemoFilterContext(request, cancellation.Token);
            var nextCalledWhileHeld = false;
            var blocked = filter.InvokeAsync(blockedContext, _ =>
            {
                nextCalledWhileHeld = true;
                return ValueTask.FromResult<object?>("next");
            }).AsTask();

            var completed = await Task.WhenAny(blocked, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.AreNotSame(blocked, completed,
                "The filter must acquire memo-id:notes-1 before calling the endpoint delegate.");
            Assert.IsFalse(nextCalledWhileHeld);

            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await blocked);
        }
        finally
        {
            await held.DisposeAsync();
        }

        var requestAfterRelease = new CreateMemoRequest(new CreateMemoInputDto(
            "author-1", "Note", "lock boundary probe", DateTimeOffset.UtcNow,
            [new MemoSubjectDto("Horse", "horse-probe")], MemoId: "notes-1"));
        var nextCalledAfterRelease = false;
        var result = await _app.Services.GetRequiredService<RaceWriteEndpointFilter>().InvokeAsync(
            CreateMemoFilterContext(requestAfterRelease, CancellationToken.None), _ =>
            {
                nextCalledAfterRelease = true;
                return ValueTask.FromResult<object?>("next");
            });

        Assert.AreEqual("next", result);
        Assert.IsTrue(nextCalledAfterRelease,
            "After the lock is released, the filter must pass the same nested opaque memo ID to the endpoint delegate.");
    }

    private static EndpointFilterInvocationContext CreateMemoFilterContext(CreateMemoRequest request, CancellationToken token)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/memos";
        httpContext.RequestAborted = token;
        return EndpointFilterInvocationContext.Create(httpContext, request);
    }
}
