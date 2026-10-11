using HorseRacingPrediction.ApiClient.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace HorseRacingPrediction.ApiClient.Tests;

[TestClass]
public sealed class HorseIdentityDiagnosticClientTests
{
    [TestMethod]
    public async Task GetDiagnostic_UsesExactGetPathWithoutBodyOrQuery_AndPreservesRepeatedEvidence()
    {
        const string horseId = "horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20";
        const string json = """
            {
              "diagnostic": {
                "horseId": "horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20",
                "eventStoreObservedFromUtc": "2026-10-11T01:00:00Z",
                "eventStoreObservedToUtc": "2026-10-11T01:01:00Z",
                "collectionStoreObservedFromUtc": "2026-10-11T01:02:00Z",
                "collectionStoreObservedToUtc": "2026-10-11T01:03:00Z",
                "currentRecordStatus": "Supported",
                "currentRegisteredName": "サンプル馬",
                "currentNormalizedName": "サンプル馬",
                "currentBirthDate": "2022-05-01",
                "registrationStatus": "Supported",
                "initialRegisteredName": "サンプル馬",
                "initialNormalizedName": "サンプル馬",
                "initialBirthDate": "2022-05-01",
                "sources": [
                  { "origin": "ProfileReadModel", "status": "Supported", "sourceIdentity": "jra-horse:abc", "normalizedSourceIdentity": "jra-horse:abc", "sourceUrlStatus": "Missing", "sourceUrlHorseIdentity": null },
                  { "origin": "SourceUrl", "status": "Supported", "sourceIdentity": null, "normalizedSourceIdentity": null, "sourceUrlStatus": "Supported", "sourceUrlHorseIdentity": "jra-horse:def" }
                ],
                "comparisons": [
                  { "rule": "SourceIdentity", "inputField": "sourceIdentity", "evidenceStatus": "Supported", "candidateHorseId": "horse-abc", "matchStatus": "Conflicting" }
                ],
                "evidenceBlockers": [ "No migration is implied." ],
                "sourcesTruncated": false,
                "coverage": "Complete"
              }
            }
            """;
        var handler = new ApiClientTestCapturingHandler(_ => new ApiClientTestCapturedResponse(HttpStatusCode.OK, json));
        using var services = CreateServices(handler);

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<IIdentityApi>()
            .GetHorseIdentityDiagnosticAsync(new(horseId));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNull(response.Error);
        var diagnostic = response.Content?.Diagnostic;
        Assert.IsNotNull(diagnostic);
        Assert.AreEqual(horseId, diagnostic.HorseId);
        Assert.AreEqual(TimeSpan.Zero, diagnostic.EventStoreObservedFromUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, diagnostic.EventStoreObservedToUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, diagnostic.CollectionStoreObservedFromUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, diagnostic.CollectionStoreObservedToUtc.Offset);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-11T01:00:00Z"), diagnostic.EventStoreObservedFromUtc);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-11T01:01:00Z"), diagnostic.EventStoreObservedToUtc);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-11T01:02:00Z"), diagnostic.CollectionStoreObservedFromUtc);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-11T01:03:00Z"), diagnostic.CollectionStoreObservedToUtc);
        Assert.AreEqual(new DateOnly(2022, 5, 1), diagnostic.CurrentBirthDate);
        Assert.HasCount(2, diagnostic.Sources);
        Assert.AreEqual("ProfileReadModel", diagnostic.Sources[0].Origin);
        Assert.AreEqual("SourceUrl", diagnostic.Sources[1].Origin);
        Assert.AreEqual("jra-horse:def", diagnostic.Sources[1].SourceUrlHorseIdentity);
        Assert.AreEqual("Conflicting", diagnostic.Comparisons[0].MatchStatus);

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual($"/api/v2/admin/identity-diagnostics/horses/{horseId}", request.RequestUri!.AbsolutePath);
        Assert.IsEmpty(request.RequestUri.Query);
        Assert.IsNull(request.Body);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    public async Task GetDiagnostic_PreservesHttpFailureStatusAndDoesNotInventContent(HttpStatusCode status)
    {
        var handler = new ApiClientTestCapturingHandler(_ => new ApiClientTestCapturedResponse(status, "{\"detail\":\"safe failure\"}"));
        using var services = CreateServices(handler);

        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<IIdentityApi>()
            .GetHorseIdentityDiagnosticAsync(new("horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20"));

        Assert.AreEqual(status, response.StatusCode);
        Assert.IsNull(response.Content);
        Assert.IsNotNull(response.Error);
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
}
