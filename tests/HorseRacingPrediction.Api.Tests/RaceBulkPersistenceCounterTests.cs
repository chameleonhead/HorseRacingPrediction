using System.Data.Common;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Contracts;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class RaceBulkPersistenceCounterTests
{
    [TestMethod]
    public async Task CommitFailure_RollsBackRaceAndReturnsFailedOutcome()
    {
        var failure = new CommitFailureInterceptor();
        var (app, client) = await TestApplicationFactory.CreateAsync(interceptors: [failure]);
        await using var lifetime = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        failure.FailNextCommit = true;

        var response = await http.PostAsJsonAsync("/api/races/result-bulk",
            new DeclareRaceResultBulkRequest(new DateOnly(2026, 9, 16), $"FAIL-{Guid.NewGuid():N}", 1,
                "commit failure"));
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();

        response.EnsureSuccessStatusCode();
        Assert.IsNotNull(body);
        Assert.IsNotEmpty(body.Errors);
        Assert.AreEqual(0, CountEvents(app));
    }

    [TestMethod]
    public async Task EighteenExistingSubjects_UsesThreeSetQueriesAndOneWriteTransaction()
    {
        var counter = new PersistenceCounters();
        var (app, client) = await TestApplicationFactory.CreateAsync(
            interceptors: [new SubjectQueryInterceptor(counter), new TransactionCounterInterceptor(counter)]);
        await using var lifetime = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var key = Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 9, 16);
        var entries = new List<RaceResultEntryBulkDto>();
        for (var number = 1; number <= 18; number++)
        {
            var horseName = $"計測馬-{key}-{number}";
            var jockeyName = $"計測騎手-{key}-{number}";
            var trainerName = $"計測調教師-{key}-{number}";
            await http.PutAsJsonAsync($"/api/horses/{HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId("horse", HorseRacingPrediction.ApiClient.DeterministicIdGenerator.NormalizeKey(horseName))}",
                new UpdateHorseProfileRequest(horseName, horseName, "M", null));
            await http.PutAsJsonAsync($"/api/jockeys/{HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId("jockey", HorseRacingPrediction.ApiClient.DeterministicIdGenerator.NormalizeKey(jockeyName))}",
                new UpdateJockeyProfileRequest(jockeyName, jockeyName, "JRA"));
            await http.PutAsJsonAsync($"/api/trainers/{HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId("trainer", HorseRacingPrediction.ApiClient.DeterministicIdGenerator.NormalizeKey(trainerName))}",
                new UpdateTrainerProfileRequest(trainerName, trainerName, "JRA"));
            entries.Add(new(number, number, null, null, null, null, null,
                horseName, jockeyName, trainerName));
        }
        counter.Reset();

        var response = await http.PostAsJsonAsync("/api/races/result-bulk",
            new DeclareRaceResultBulkRequest(date, $"COUNT-{key}", 1, "永続化計測", 18,
                WinningHorseName: entries[0].HorseName, DeclaredAt: DateTimeOffset.UtcNow, Entries: entries));

        response.EnsureSuccessStatusCode();
        Assert.AreEqual(3, counter.SubjectSelects);
        Assert.AreEqual(1, counter.TransactionCommits);
    }

    private sealed class PersistenceCounters
    {
        public int SubjectSelects { get; set; }
        public int TransactionCommits { get; set; }

        public void Reset()
        {
            SubjectSelects = 0;
            TransactionCommits = 0;
        }
    }

    private sealed class SubjectQueryInterceptor(PersistenceCounters counters) : DbCommandInterceptor
    {

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Horses\"", StringComparison.Ordinal)
                || command.CommandText.Contains("FROM \"Jockeys\"", StringComparison.Ordinal)
                || command.CommandText.Contains("FROM \"Trainers\"", StringComparison.Ordinal))
                counters.SubjectSelects++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransactionCounterInterceptor(PersistenceCounters counters) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            counters.TransactionCommits++;
            return Task.CompletedTask;
        }
    }

    private sealed class CommitFailureInterceptor : SaveChangesInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new InvalidOperationException("Injected commit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private static int CountEvents(WebApplication app)
    {
        var provider = app.Services.GetRequiredService<EventFlow.EntityFramework.IDbContextProvider<
            HorseRacingPrediction.Infrastructure.Persistence.EventStoreDbContext>>();
        using var db = provider.CreateContext();
        return db.Set<EventFlow.EntityFramework.EventStores.EventEntity>().Count();
    }
}
