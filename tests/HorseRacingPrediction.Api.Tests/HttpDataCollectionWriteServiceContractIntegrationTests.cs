using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.Memos;
using HorseRacingPrediction.Contracts.Predictions;
using HorseRacingPrediction.Contracts.Races;
using HorseRacingPrediction.Contracts.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HttpDataCollectionWriteServiceContractIntegrationTests
{
    [TestMethod]
    public async Task PublicWriteService_BulkCallUnwrapsResultEnvelope()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using (http)
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var writer = new HttpDataCollectionWriteService(http, new AgentAcquisitionStatusRecorder());
            var token = Guid.NewGuid().ToString("N");
            var request = new DeclareRaceResultBulkRequest(new(
                new DateOnly(2043, 5, 20), "TOKYO", 8, $"Bulk adapter race {token}"));

            var result = await writer.DeclareRaceResultBulkAsync(request);

            Assert.IsTrue(result.CorePersisted, string.Join(";", result.Errors));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.RaceId));
            var persisted = await http.GetFromJsonAsync<GetRaceResponse>($"/api/races/{result.RaceId}");
            Assert.IsNotNull(persisted);
            Assert.AreEqual(result.RaceId, persisted.Race.RaceId);
            Assert.AreEqual($"Bulk adapter race {token}", persisted.Race.RaceName);
        }
    }

    [TestMethod]
    public async Task PublicWriteService_EnsuresMissingProfilesAndReadsExistingProfilesThroughWrappers()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using (http)
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var writer = new HttpDataCollectionWriteService(http, new AgentAcquisitionStatusRecorder());
            var token = Guid.NewGuid().ToString("N");
            var raceId = await writer.UpsertRaceAsync(
                "2044-05-20", "TOKYO", 8, $"Dangling profile race {token}", 1, "G1", "TURF", 2000, "RIGHT");
            var horseName = $"Missing horse {token}";
            var jockeyName = $"Missing jockey {token}";
            var trainerName = $"Missing trainer {token}";
            using var identityResponse = await http.PostAsJsonAsync(
                "/api/identity/horse", new ResolveHorseIdentityRequest(new(horseName)));
            identityResponse.EnsureSuccessStatusCode();
            var resolved = (await identityResponse.Content.ReadFromJsonAsync<ResolveHorseIdentityResponse>())?.Identity;
            Assert.IsNotNull(resolved);
            var jockeyId = $"jockey-{Guid.NewGuid():D}";
            var trainerId = $"trainer-{Guid.NewGuid():D}";
            var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, resolved.Id);
            using var seededEntry = await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
                new RegisterEntryRequest(new RegisterEntryInputDto(
                    resolved.Id, 1, jockeyId, trainerId, 1, 56m, "M", 4, 450m, 0m,
                    EntryId: entryId, HorseName: horseName, JockeyName: jockeyName, TrainerName: trainerName))
                {
                    RaceId = raceId
                });
            seededEntry.EnsureSuccessStatusCode();

            var provider = app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>();
            using (var db = provider.CreateContext())
            {
                db.RemoveRange(db.Set<HorseReadModel>().Where(x => x.HorseId == resolved.Id));
                db.RemoveRange(db.Set<JockeyReadModel>().Where(x => x.JockeyId == jockeyId));
                db.RemoveRange(db.Set<TrainerReadModel>().Where(x => x.TrainerId == trainerId));
                db.SaveChanges();
            }
            Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/horses/{resolved.Id}")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/jockeys/{jockeyId}")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/trainers/{trainerId}")).StatusCode);

            // The existing entry path first sees the seeded Horse and Trainer IDs with no profiles,
            // then creates those profiles. Jockey upsert uses the same incoming display name path.
            await writer.UpsertRaceEntryAsync(
                raceId, 1, horseName, jockeyName, trainerName, 1, 56m, "M", 4, 450m, 0m);

            var context = await http.GetFromJsonAsync<GetRacePredictionContextResponse>($"/api/races/{raceId}/context");
            Assert.IsNotNull(context);
            var entry = context.Context.Entries.Single();
            Assert.AreEqual(resolved.Id, entry.HorseId);
            Assert.AreEqual(trainerId, entry.TrainerId);
            var horse = await http.GetFromJsonAsync<GetHorseProfileResponse>($"/api/horses/{resolved.Id}");
            var jockey = await http.GetFromJsonAsync<GetJockeyProfileResponse>($"/api/jockeys/{entry.JockeyId}");
            var trainer = await http.GetFromJsonAsync<GetTrainerProfileResponse>($"/api/trainers/{trainerId}");
            Assert.IsNotNull(horse);
            Assert.IsNotNull(jockey);
            Assert.IsNotNull(trainer);
            Assert.AreEqual(horseName, horse.Horse.RegisteredName);
            Assert.AreEqual(jockeyName, jockey.Jockey.DisplayName);
            Assert.AreEqual(trainerName, trainer.Trainer.DisplayName);
        }
    }

    [TestMethod]
    public async Task PublicWriteService_RescheduleSendsNestedReplacementAndPersistsLineage()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using (http)
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var writer = new HttpDataCollectionWriteService(http, new AgentAcquisitionStatusRecorder());
            var token = Guid.NewGuid().ToString("N");
            var sourceId = await writer.UpsertRaceAsync(
                "2042-05-20", "TOKYO", 8, $"Rescheduled source {token}", null, "G1", "TURF", 2000, "RIGHT");
            var replacementId = await writer.UpsertRaceAsync(
                "2042-05-27", "TOKYO", 8, $"Rescheduled replacement {token}", null, "G1", "TURF", 2000, "RIGHT");

            await writer.MarkRaceRescheduledAsync(sourceId, replacementId);

            var source = await http.GetFromJsonAsync<GetRaceResponse>($"/api/races/{sourceId}");
            var replacement = await http.GetFromJsonAsync<GetRaceResponse>($"/api/races/{replacementId}");
            Assert.IsNotNull(source);
            Assert.IsNotNull(replacement);
            Assert.AreEqual(RaceStatus.Rescheduled, source.Race.Status);
            Assert.AreEqual(replacementId, source.Race.ReplacementRaceId);
            Assert.AreEqual(replacementId, replacement.Race.RaceId);
            Assert.AreEqual(RaceStatus.Draft, replacement.Race.Status);
        }
    }

    [TestMethod]
    public async Task PublicWriteService_UsesNestedRequestsAndReadsWrappedResponsesAgainstApiServer()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using (http)
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var writer = new HttpDataCollectionWriteService(http, new AgentAcquisitionStatusRecorder());
            var token = Guid.NewGuid().ToString("N");
            var raceName = $"Adapter race {token}";
            var updatedRaceName = $"Updated adapter race {token}";
            var horseName = $"Adapter horse {token}";
            var jockeyName = $"Adapter jockey {token}";
            var trainerName = $"Adapter trainer {token}";

            var raceId = await writer.UpsertRaceAsync(
                "2041-05-20", "TOKYO", 8, raceName, 1, "G1", "TURF", 2000, "RIGHT");
            Assert.IsNotNull(await http.GetFromJsonAsync<GetRacePredictionContextResponse>(
                $"/api/races/{raceId}/context"));

            var sameRaceId = await writer.UpsertRaceAsync(
                "2041-05-20", "TOKYO", 8, updatedRaceName, 1, "G1", "TURF", 2000, "RIGHT");
            Assert.AreEqual(raceId, sameRaceId);

            var horseId = await writer.UpsertHorseProfileAsync(
                horseName, $"normalized-{token}", "F", "2021-02-04", $"Owner {token}",
                $"Breeder {token}", $"Sire {token}", $"Dam {token}", $"Damsire {token}", "Chestnut");
            var jockeyId = await writer.UpsertJockeyAsync(jockeyName, null, "JRA");
            var trainerId = await writer.UpsertTrainerAsync(trainerName, null, "JRA");

            var horse = await http.GetFromJsonAsync<GetHorseProfileResponse>($"/api/horses/{horseId}");
            var jockey = await http.GetFromJsonAsync<GetJockeyProfileResponse>($"/api/jockeys/{jockeyId}");
            var trainer = await http.GetFromJsonAsync<GetTrainerProfileResponse>($"/api/trainers/{trainerId}");
            Assert.IsNotNull(horse);
            Assert.IsNotNull(jockey);
            Assert.IsNotNull(trainer);
            Assert.AreEqual(horseId, horse.Horse.HorseId);
            Assert.AreEqual($"Owner {token}", horse.Horse.OwnerName);
            Assert.AreEqual($"Breeder {token}", horse.Horse.BreederName);
            Assert.AreEqual(jockeyId, jockey.Jockey.JockeyId);
            Assert.AreEqual(jockeyName, jockey.Jockey.DisplayName);
            Assert.AreEqual(trainerId, trainer.Trainer.TrainerId);
            Assert.AreEqual(trainerName, trainer.Trainer.DisplayName);

            await writer.UpsertRaceEntryAsync(
                raceId, 1, horseName, jockeyName, trainerName, 1, 56.5m, "F", 5, 460m, 2m, $"Owner {token}");
            await writer.UpsertRaceEntryAsync(
                raceId, 1, horseName, jockeyName, trainerName, 2, 57m, "F", 5, 462m, 4m, $"Updated owner {token}");

            var context = await http.GetFromJsonAsync<GetRacePredictionContextResponse>($"/api/races/{raceId}/context");
            Assert.IsNotNull(context);
            Assert.AreEqual(raceId, context.Context.RaceId);
            Assert.AreEqual(updatedRaceName, context.Context.RaceName);
            var entry = context.Context.Entries.Single();
            Assert.AreEqual(horseId, entry.HorseId);
            Assert.AreEqual(jockeyId, entry.JockeyId);
            Assert.AreEqual(trainerId, entry.TrainerId);
            Assert.AreEqual(2, entry.GateNumber);
            Assert.AreEqual(57m, entry.AssignedWeight);
            Assert.AreEqual(462m, entry.DeclaredWeight);
            Assert.AreEqual(4m, entry.DeclaredWeightDiff);
            Assert.AreEqual($"Updated owner {token}", entry.OwnerName);

            var declaredAt = DateTimeOffset.UtcNow;
            await writer.DeclareRaceResultAsync(raceId, horseName, declaredAt.ToString("O", CultureInfo.InvariantCulture), horseId);
            await writer.DeclareRaceEntryResultAsync(raceId, horseId, 1, "2:01.0", "1", "34.1", null, 10000m);
            await writer.DeclareRacePayoutsAsync(raceId, "[{\"combination\":\"1\",\"amount\":250}]", null, null, null, null);
            var observedAt = declaredAt.AddMinutes(1);
            await writer.RecordWeatherObservationAsync(raceId, observedAt, "SUNNY", "Clear", 21.5m, 55m, "N", 3.2m);
            await writer.RecordTrackConditionObservationAsync(raceId, observedAt, "GOOD", "FAST", "Firm");

            var raceResponse = await http.GetFromJsonAsync<GetRaceResponse>($"/api/races/{raceId}");
            Assert.IsNotNull(raceResponse);
            Assert.AreEqual(updatedRaceName, raceResponse.Race.RaceName);
            Assert.AreEqual(horseName, raceResponse.Race.WinningHorseName);
            Assert.AreEqual(horseId, raceResponse.Race.WinningHorseId);
            Assert.AreEqual(1, raceResponse.Race.EntryResults.Count);
            Assert.AreEqual(1, raceResponse.Race.EntryResults[0].FinishPosition);
            Assert.AreEqual("2:01.0", raceResponse.Race.EntryResults[0].OfficialTime);
            Assert.AreEqual(10000m, raceResponse.Race.EntryResults[0].PrizeMoney);
            Assert.AreEqual(250m, raceResponse.Race.PayoutResult!.WinPayouts.Single().Amount);
            Assert.AreEqual("SUNNY", raceResponse.Race.WeatherObservations.Single().WeatherCode);
            Assert.AreEqual(21.5m, raceResponse.Race.WeatherObservations.Single().TemperatureCelsius);
            Assert.AreEqual("GOOD", raceResponse.Race.TrackConditionObservations.Single().TurfConditionCode);
            Assert.AreEqual("FAST", raceResponse.Race.TrackConditionObservations.Single().DirtConditionCode);
        }
    }

    [TestMethod]
    public async Task PublicMemoAdapters_CreateConflictUpdateAndReadPersistedMemoThroughApiServer()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using (http)
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var api = new AdminApiClient(http,
                new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
                Options.Create(new ApiKeyOptions { HeaderName = "X-Api-Key", Key = TestApplicationFactory.TestApiKey }));
            var raceId = $"race-{Guid.NewGuid():D}";
            var memoId = $"memo-{Guid.NewGuid():D}";
            var created = await api.CreateMemoAsync(new CreateMemoRequest(new CreateMemoInputDto(
                "adapter-test", "RaceNote", "initial content", DateTimeOffset.UtcNow,
                [new MemoSubjectDto("Race", raceId)], MemoId: memoId)));
            Assert.IsTrue(created.Success, string.Join(";", created.Errors));
            Assert.AreEqual(memoId, created.Value);

            var writer = new HttpMemoWriteService(http);
            Assert.AreEqual(memoId, await writer.CreateOrUpdateRaceMemoAsync(
                raceId, "RaceNote", "updated by collector", "adapter-test", memoId));

            var memos = await api.GetMemosBySubjectAsync("Race", raceId);
            var memo = memos.Single(x => x.MemoId == memoId);
            Assert.AreEqual("updated by collector", memo.Content);
            Assert.AreEqual("RaceNote", memo.MemoType);
            CollectionAssert.AreEquivalent(new[] { raceId }, memo.Subjects.Select(x => x.SubjectId).ToArray());
        }
    }

    [TestMethod]
    public async Task PublicPredictionWriteAdapter_CreatesMarksAddsRationaleAndFinalizesPersistedTicket()
    {
        var (app, http) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using (http)
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var token = Guid.NewGuid().ToString("N");
            var raceId = $"race-{Guid.NewGuid():D}";
            var horseId = $"horse-{Guid.NewGuid():D}";
            var horseName = $"Adapter prediction horse {token}";
            (await http.PostAsJsonAsync("/api/races", new CreateRaceRequest(new(
                new DateOnly(2045, 5, 20), "TOKYO", 8, $"Prediction adapter race {token}", raceId))))
                .EnsureSuccessStatusCode();
            Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync(
                "/api/horses", SubjectRequestFactory.RegisterHorse(horseName, horseName, "M",
                    new DateOnly(2021, 4, 12), horseId))).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish",
                new PublishRaceCardRequest(new(1)) { RaceId = raceId })).StatusCode);
            var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, horseId);
            Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
                new RegisterEntryRequest(new RegisterEntryInputDto(
                    horseId, 1, null, null, 1, 56m, "M", 5, 451m, 1m,
                    EntryId: entryId, HorseName: horseName))
                { RaceId = raceId })).StatusCode);
            var context = await http.GetFromJsonAsync<GetRacePredictionContextResponse>($"/api/races/{raceId}/context");
            Assert.IsNotNull(context);

            var writer = new HttpPredictionWriteService(http);
            var ticketId = await writer.CreateBoundPredictionTicketAsync(
                raceId, "Human", "adapter-test", 0.83m, "persisted collector ticket",
                context.Context.EntryAssignmentFingerprint);
            await writer.AddPredictionMarkAsync(ticketId, entryId, "◎", 1, 91.5m, "distinct persisted mark");
            await writer.AddPredictionRationaleAsync(ticketId, "Horse", horseId, "SPEED_INDEX", "117",
                "persisted rationale");
            await writer.FinalizePredictionTicketAsync(ticketId);

            var persisted = await http.GetFromJsonAsync<GetPredictionTicketResponse>($"/api/predictions/{ticketId}");
            Assert.IsNotNull(persisted);
            Assert.AreEqual(ticketId, persisted.PredictionTicket.PredictionTicketId);
            Assert.AreEqual(raceId, persisted.PredictionTicket.RaceId);
            Assert.AreEqual(TicketStatus.Finalized, persisted.PredictionTicket.TicketStatus);
            var mark = persisted.PredictionTicket.Marks.Single();
            Assert.AreEqual(entryId, mark.EntryId);
            Assert.AreEqual("◎", mark.MarkCode);
            Assert.AreEqual(91.5m, mark.Score);
            Assert.AreEqual("distinct persisted mark", mark.Comment);
            using var db = app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext();
            var ticketEvents = await db.Set<EventEntity>().Where(x => x.AggregateId == ticketId)
                .Select(x => x.Data).ToArrayAsync();
            Assert.IsTrue(ticketEvents.Any(x => x.Contains("SPEED_INDEX", StringComparison.Ordinal)
                && x.Contains("persisted rationale", StringComparison.Ordinal)));
        }
    }
}
