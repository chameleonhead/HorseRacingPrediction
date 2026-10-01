using HorseRacingPrediction.Collector.CollectionPlatform;
using System.Net;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraSubjectProfileApiClientTests
{
    [TestMethod]
    public async Task SaveAsync_PutsCanonicalProfileResourceWithProfileBody()
    {
        using var client = new HttpClient(new RecordingHandler())
        {
            BaseAddress = new Uri("https://example.test/"),
        };
        var profile = new JraSubjectProfileDto("Horse", "テスト馬", "source-1", "https://example.test/horse",
            new() { ["父"] = "父馬" }, new DateTimeOffset(2026, 9, 28, 1, 2, 3, TimeSpan.Zero));

        await new JraSubjectProfileApiClient(client).SaveAsync("Horse", "horse/1", profile, CancellationToken.None);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual("/api/v2/admin/subjects/Horse/horse%2F1/profile", request.RequestUri!.AbsolutePath);
            Assert.AreEqual("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var profile = body.RootElement.GetProperty("profile");
            Assert.AreEqual("Horse", profile.GetProperty("subjectType").GetString());
            Assert.AreEqual("テスト馬", profile.GetProperty("name").GetString());
            Assert.AreEqual("父馬", profile.GetProperty("fields").GetProperty("父").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
