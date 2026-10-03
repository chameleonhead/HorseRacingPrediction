using EventFlow;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Repairs;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SubjectIdentificationCandidateEndpointTests
{
    [TestMethod]
    public async Task Apply_ReplaysFromLedgerAndLeavesSourceLessNamesakeUntouched()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var bus = app.Services.GetRequiredService<ICommandBus>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse, 1, "initial", false);

        const string name = "構造化候補馬";
        var sourceLessId = DeterministicIdGenerator.BuildHorseId(name);
        await bus.PublishAsync(new RegisterHorseCommand(new HorseId(sourceLessId), name,
            JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", name)), CancellationToken.None);
        var resource = new ResourceKey(CollectionResourceType.Horse, "JRA", "ambiguous-resource");
        var definition = new CollectionDefinitionId("horse-profile");
        var firstUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000001/03";
        var secondUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000002/04";
        var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Discovery,
            DateTimeOffset.UtcNow, attributes: new Dictionary<string, string>
            {
                ["name"] = name,
                ["discoveredFromType"] = "Race",
                ["discoveredFromProvider"] = "JRA",
                ["discoveredFromId"] = "race-origin",
                ["discoveredFromReason"] = "Discovery",
            });
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId.Value, lease.LeaseToken,
            DateTimeOffset.UtcNow.AddSeconds(1), new(CollectionAttemptResult.ResourceNotFound,
                ErrorCode: "SubjectNotIdentified", ErrorMessage: "candidate text is not an API contract",
                PageIdentification: "SubjectIdentification:MultipleCandidates",
                IdentificationCandidates: [new(name, firstUrl, "公開検索候補"), new(name, secondUrl, "公開検索候補")])));

        var failure = (await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10))
            .Single();
        var request = new ApplySubjectIdentificationCandidateRequest(failure.NotificationId,
            new SubjectIdentificationCandidateSelectionInputDto(name, firstUrl, "公開検索候補"));
        var reservation = await store.ReserveSubjectIdentificationCandidateAsync(
            failure.NotificationId, new(name, firstUrl, "公開検索候補"),
            new(CollectionResourceType.Horse, "JRA", DeterministicIdGenerator.BuildHorseId(name, firstUrl)),
            definition, JraSourceIdentity.NormalizeHorseUrl(firstUrl)!, "test-selector",
            DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.IsFalse(reservation.IsFinalized,
            "A reservation without a task must remain resumable after a process interruption.");
        using var first = await client.PostAsJsonAsync(
            "/api/admin/repairs/subject-identification/candidate/apply", request);
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode, await first.Content.ReadAsStringAsync());
        var applied = await first.Content.ReadFromJsonAsync<ApplySubjectIdentificationCandidateResponse>();
        Assert.IsNotNull(applied);
        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId(name, firstUrl),
            applied.Application.CanonicalResource.Id);
        var canonicalDetail = await client.GetFromJsonAsync<GetCollectionResourceDetailResponse>(
            $"/api/v2/admin/collection/resources/Horse/JRA/{applied.Application.CanonicalResource.Id}/definitions/horse-profile");
        Assert.IsNotNull(canonicalDetail?.Resource.Origin);
        Assert.AreEqual("race-origin", canonicalDetail.Resource.Origin.Resource.Id);
        Assert.AreEqual("Discovery", canonicalDetail.Resource.Origin.Reason);
        Assert.IsFalse(canonicalDetail.Resource.Origin.DetailsAvailable);

        var canonicalLease = await store.AcquireAsync(applied.Application.CanonicalTaskId, 1,
            DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(canonicalLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(applied.Application.CanonicalTaskId,
            canonicalLease.LeaseToken, DateTimeOffset.UtcNow.AddMinutes(1).AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, ErrorCode: "SubjectNotIdentified",
                PageIdentification: "SubjectIdentification:ProfileNameMismatch")));
        var reopened = await store.GetActionableFailureNotificationsAsync(
            DateTimeOffset.UtcNow.AddMinutes(2), 100);
        Assert.IsTrue(reopened.Any(x => x.NotificationId == failure.NotificationId),
            "Canonical recovery failure must reopen the original notification.");

        using var replay = await client.PostAsJsonAsync(
            "/api/admin/repairs/subject-identification/candidate/apply", request);
        Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<ApplySubjectIdentificationCandidateResponse>();
        Assert.AreEqual(applied.Application.CanonicalTaskId, replayed?.Application.CanonicalTaskId);

        using var conflict = await client.PostAsJsonAsync(
            "/api/admin/repairs/subject-identification/candidate/apply",
            request with { Selection = new(name, secondUrl, "公開検索候補") });
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);

        using var tampered = await client.PostAsJsonAsync(
            "/api/admin/repairs/subject-identification/candidate/apply",
            request with { Selection = new("別の馬", firstUrl, "公開検索候補") });
        Assert.AreEqual(HttpStatusCode.Conflict, tampered.StatusCode);

        using var db = app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext();
        Assert.IsNotNull(await db.Horses.AsNoTracking().SingleOrDefaultAsync(x => x.HorseId == sourceLessId));
        Assert.IsNull(await db.Horses.AsNoTracking().SingleOrDefaultAsync(x =>
            x.HorseId == DeterministicIdGenerator.BuildHorseId(name, secondUrl)));
    }

    [TestMethod]
    public async Task Apply_CompetingApiInstancesReserveBeforeRegisteringTheLosingCanonicalHorse()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"candidate-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "domain.db")}";
        try
        {
            var (app1, client1) = await TestApplicationFactory.CreateAsync(
                connectionString, aggregationDelayMilliseconds: 0);
            var (app2, client2) = await TestApplicationFactory.CreateAsync(
                connectionString, aggregationDelayMilliseconds: 0);
            await using var lifetime1 = app1;
            await using var lifetime2 = app2;
            client1.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            client2.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            var store = app1.Services.GetRequiredService<CollectionPlatformStore>();
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse,
                1, "initial", false);
            var failedResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "concurrent-ambiguous-resource");
            var definition = new CollectionDefinitionId("horse-profile");
            var receipt = await store.RequestAsync(failedResource, definition, 1, CollectionReason.Discovery,
                DateTimeOffset.UtcNow, attributes: new Dictionary<string, string> { ["name"] = "競合候補馬" });
            var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5));
            Assert.IsNotNull(lease);
            Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId.Value, lease.LeaseToken,
                DateTimeOffset.UtcNow.AddSeconds(1), new(CollectionAttemptResult.ResourceNotFound,
                    ErrorCode: "SubjectNotIdentified", PageIdentification: "SubjectIdentification:MultipleCandidates",
                    IdentificationCandidates: [
                        new("競合候補馬", "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000101/01", "候補A"),
                        new("競合候補馬", "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000102/02", "候補B")])));
            var failure = (await store.GetActionableFailureNotificationsAsync(
                DateTimeOffset.UtcNow.AddMinutes(1), 10)).Single();
            var firstUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000101/01";
            var secondUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000102/02";
            var request1 = new ApplySubjectIdentificationCandidateRequest(failure.NotificationId,
                new SubjectIdentificationCandidateSelectionInputDto("競合候補馬", firstUrl, "候補A"));
            var request2 = request1 with
            {
                Selection = new SubjectIdentificationCandidateSelectionInputDto("競合候補馬", secondUrl, "候補B")
            };

            var responses = await Task.WhenAll(
                client1.PostAsJsonAsync("/api/admin/repairs/subject-identification/candidate/apply", request1),
                client2.PostAsJsonAsync("/api/admin/repairs/subject-identification/candidate/apply", request2));
            Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.Accepted),
                string.Join("; ", responses.Select(x => x.StatusCode)));
            Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict),
                string.Join("; ", responses.Select(x => x.StatusCode)));
            var winnerResponse = responses.Single(x => x.StatusCode == HttpStatusCode.Accepted);
            var winner = await winnerResponse.Content.ReadFromJsonAsync<ApplySubjectIdentificationCandidateResponse>();
            Assert.IsNotNull(winner);
            var winnerId = winner.Application.CanonicalResource.Id;
            var losingId = winner.Application.SelectedUrl == firstUrl
                ? DeterministicIdGenerator.BuildHorseId("競合候補馬", secondUrl)
                : DeterministicIdGenerator.BuildHorseId("競合候補馬", firstUrl);
            using var db = app1.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext();
            Assert.IsNotNull(await db.Horses.AsNoTracking().SingleOrDefaultAsync(x => x.HorseId == winnerId));
            Assert.IsNull(await db.Horses.AsNoTracking().SingleOrDefaultAsync(x => x.HorseId == losingId),
                "The losing candidate must not register a canonical Horse before durable reservation conflict.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task Reservation_UsesDatabaseClaimAcrossIndependentStoreInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"candidate-store-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
                DatabaseFileName = "shared.db",
            });
            var storeA = new CollectionPlatformStore(options);
            var storeB = new CollectionPlatformStore(options);
            await storeA.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse,
                1, "initial", false);
            var definition = new CollectionDefinitionId("horse-profile");
            var failedResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "store-race-resource");
            var receipt = await storeA.RequestAsync(failedResource, definition, 1, CollectionReason.Discovery,
                DateTimeOffset.UtcNow, attributes: new Dictionary<string, string> { ["name"] = "DB予約競合馬" });
            var lease = await storeA.AcquireAsync(receipt.TaskId!.Value, 1, DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5));
            Assert.IsNotNull(lease);
            Assert.IsTrue(await storeA.CompleteAttemptAsync(receipt.TaskId.Value, lease.LeaseToken,
                DateTimeOffset.UtcNow.AddSeconds(1), new(CollectionAttemptResult.ResourceNotFound,
                    ErrorCode: "SubjectNotIdentified", PageIdentification: "SubjectIdentification:MultipleCandidates",
                    IdentificationCandidates: [
                        new("DB予約競合馬", "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000201/01", "候補A"),
                        new("DB予約競合馬", "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000202/02", "候補B")])));
            var failure = (await storeA.GetActionableFailureNotificationsAsync(
                DateTimeOffset.UtcNow.AddMinutes(1), 10)).Single();
            var first = new SubjectIdentificationCandidate("DB予約競合馬",
                "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000201/01", "候補A");
            var second = new SubjectIdentificationCandidate("DB予約競合馬",
                "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud100000000202/02", "候補B");

            var outcomes = await Task.WhenAll(
                Reserve(storeA, failure.NotificationId, first, definition),
                Reserve(storeB, failure.NotificationId, second, definition));
            Assert.AreEqual(1, outcomes.Count(x => x is SubjectIdentificationCandidateApplication));
            Assert.AreEqual(1, outcomes.Count(x => x is SubjectIdentificationSelectionConflictException));

            var winner = outcomes.OfType<SubjectIdentificationCandidateApplication>().Single();
            var retry = await storeB.ReserveSubjectIdentificationCandidateAsync(
                failure.NotificationId, string.Equals(first.Url, winner.SelectedUrl.AbsoluteUri,
                    StringComparison.Ordinal)
                    ? first : second,
                winner.CanonicalResource, definition, winner.SelectedUrl, "retry", winner.SelectedAt);
            Assert.AreEqual(winner.SelectedUrl, retry.SelectedUrl);
            Assert.AreEqual(Guid.Empty, retry.CanonicalTaskId,
                "A reservation must remain resumable until request creation/finalization succeeds.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        static async Task<object?> Reserve(CollectionPlatformStore store, Guid notificationId,
            SubjectIdentificationCandidate candidate, CollectionDefinitionId definition)
        {
            try
            {
                var url = JraSourceIdentity.NormalizeHorseUrl(candidate.Url)!;
                return await store.ReserveSubjectIdentificationCandidateAsync(notificationId, candidate,
                    new(CollectionResourceType.Horse, "JRA",
                        DeterministicIdGenerator.BuildHorseId(candidate.Name, url.AbsoluteUri)), definition,
                    url, "store-race", DateTimeOffset.UtcNow);
            }
            catch (SubjectIdentificationSelectionConflictException exception)
            {
                return exception;
            }
        }
    }
}
