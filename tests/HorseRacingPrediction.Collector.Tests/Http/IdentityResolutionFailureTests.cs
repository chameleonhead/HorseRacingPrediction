using System.Net;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;

using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class IdentityResolutionFailureTests
{
    [TestMethod]
    [DataRow(422, "HorseIdentityEvidenceRequired")]
    [DataRow(422, "AmbiguousHorseIdentity")]
    [DataRow(409, "HorseIdentityEvidenceRequired")]
    [DataRow(409, "AmbiguousHorseIdentity")]
    public async Task KnownHorseIdentityFailure_IsIsolatedWithoutWriting(int status, string code)
    {
        var transport = new ResponseHandler(status, "{\"code\":\"" + code + "\",\"secret\":\"must-not-be-recorded\"}");
        using var http = new HttpClient(transport) { BaseAddress = new("https://api.test") };
        var writer = new HttpDataCollectionWriteService(http, new());
        var error = await Assert.ThrowsExactlyAsync<SubjectIdentityResolutionException>(() =>
            writer.UpsertHorseAsync("マテラスカイ", null, null, null));
        var completion = CollectionAttemptFailureClassifier.FromException(error);
        Assert.AreEqual(CollectionAttemptResult.PermanentFailure, completion.Result);
        Assert.AreEqual(CollectionFailureImpact.Isolated, completion.FailureImpact);
        Assert.AreEqual(code, completion.ErrorCode);
        Assert.AreEqual(status, completion.HttpStatusCode);
        StringAssert.Contains(completion.ErrorMessage!, "SubjectName=マテラスカイ");
        StringAssert.Contains(completion.ErrorMessage!, "Path=/api/identity/horse");
        Assert.IsFalse(completion.ErrorMessage!.Contains("must-not-be-recorded"));
        Assert.AreEqual(1, transport.Calls);
    }

    [TestMethod]
    [DataRow(409, "{\"code\":\"HorseIdentityConflict\"}", false)]
    [DataRow(422, "{\"code\":\"Unknown\"}", false)]
    [DataRow(422, "broken", false)]
    [DataRow(409, "[]", false)]
    [DataRow(422, "{\"code\":3}", false)]
    [DataRow(422, "{\"code\":\"HorseIdentityEvidenceRequired\"}", true)]
    [DataRow(500, "{\"code\":\"HorseIdentityEvidenceRequired\"}", false)]
    public async Task UnknownOrWrongEndpointFailure_RetainsSafetyStop(int status, string body, bool race)
    {
        using var http = new HttpClient(new ResponseHandler(status, body)) { BaseAddress = new("https://api.test") };
        var writer = new HttpDataCollectionWriteService(http, new());
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => race
            ? writer.ResolveRaceIdentityAsync(new(2026, 9, 27), "Nakayama", 1)
            : writer.UpsertHorseAsync("テスト", null, null, null));
        Assert.AreEqual(CollectionFailureImpact.StopPipeline,
            CollectionAttemptFailureClassifier.FromException(error).FailureImpact);
    }

    private sealed class ResponseHandler(int status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }
    }
}
