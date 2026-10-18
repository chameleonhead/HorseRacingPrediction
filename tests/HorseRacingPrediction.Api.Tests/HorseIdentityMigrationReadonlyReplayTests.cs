using EventFlow;
using EventFlow.Commands;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using System.Text;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationReadonlyReplayTests
{
    [TestMethod]
    public async Task VerifyAsync_ReplaysEverySupportedTypeAndMultipleRootsWithoutChangingFiles()
    {
        await using var fixture = await Fixture.CreatePopulatedAsync();
        var before = await fixture.SnapshotAsync();

        await HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            fixture.EventStorePath, fixture.Targets, CancellationToken.None);

        Assert.AreEqual(before, await fixture.SnapshotAsync());
        AssertCanRenameAfterReplay(fixture.EventStorePath);
    }

    [TestMethod]
    public async Task VerifyAsync_AcceptsZeroTargetsOnlyWhenThereAreNoSupportedRoots()
    {
        await using var fixture = await Fixture.CreateEmptyAsync();
        var before = await fixture.SnapshotAsync();

        await HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            fixture.EventStorePath, [], CancellationToken.None);

        Assert.AreEqual(before, await fixture.SnapshotAsync());
    }

    [TestMethod]
    public async Task VerifyAsync_RejectsDuplicateUnsupportedAndInvalidExpectedTargets()
    {
        var path = Path.Combine(Path.GetTempPath(), "not-opened-eventstore.db");
        var horse = Target(HorseIdentityMigrationReplayAggregateKind.Horse, "horse-a", 1);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            path, [horse, horse], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            path, [Target((HorseIdentityMigrationReplayAggregateKind)99, "horse-a", 1)], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            path, [Target(HorseIdentityMigrationReplayAggregateKind.Horse, "horse-a", 0)], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            path, [Target(HorseIdentityMigrationReplayAggregateKind.Horse, " ", 1)], CancellationToken.None));
    }

    [TestMethod]
    public async Task VerifyAsync_RejectsMissingAndExtraSupportedRoots()
    {
        await using var fixture = await Fixture.CreatePopulatedAsync();
        var omitted = fixture.Targets.Skip(1).ToArray();
        var extra = fixture.Targets.Append(Target(
            HorseIdentityMigrationReplayAggregateKind.Horse, "horse-not-in-store", 1)).ToArray();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            fixture.EventStorePath, omitted, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            fixture.EventStorePath, extra, CancellationToken.None));
    }

    [TestMethod]
    public async Task VerifyAsync_RejectsSequenceGapsAndMalformedEventsOnIsolatedCopies()
    {
        await using var fixture = await Fixture.CreatePopulatedAsync();
        var gapCopy = fixture.CopyForMutation("gap");
        await ExecuteAsync(gapCopy,
            $"UPDATE EventEntity SET AggregateSequenceNumber = 3 WHERE AggregateId = '{Fixture.RaceId1}' AND AggregateSequenceNumber = 2;");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            gapCopy, fixture.Targets, CancellationToken.None));

        var malformedCopy = fixture.CopyForMutation("malformed");
        await ExecuteAsync(malformedCopy,
            $"UPDATE EventEntity SET Data = '{{' WHERE AggregateId = '{Fixture.HorseId1}' AND AggregateSequenceNumber = 1;");
        await AssertFailsAsync(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            malformedCopy, fixture.Targets, CancellationToken.None));
        AssertCanRenameAfterReplay(malformedCopy);

        Assert.AreEqual(fixture.SourceSnapshot, await fixture.SnapshotAsync());
    }

    [TestMethod]
    public async Task VerifyAsync_RejectsOversizedRootIdsAndEventFieldsWithoutMaterializingThem()
    {
        await using var fixture = await Fixture.CreatePopulatedAsync();
        var oversizedIdCopy = fixture.CopyForMutation("oversized-id");
        await ExecuteAsync(oversizedIdCopy,
            $"UPDATE EventEntity SET AggregateId = printf('%0513d', 0) WHERE AggregateId = '{Fixture.HorseId1}';");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            oversizedIdCopy, fixture.Targets, CancellationToken.None));

        var oversizedEventCopy = fixture.CopyForMutation("oversized-event");
        await ExecuteAsync(oversizedEventCopy,
            $"UPDATE EventEntity SET Data = zeroblob(65537) WHERE AggregateId = '{Fixture.HorseId1}' AND AggregateSequenceNumber = 1;");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            oversizedEventCopy, fixture.Targets, CancellationToken.None));

        Assert.AreEqual(fixture.SourceSnapshot, await fixture.SnapshotAsync());
    }

    [TestMethod]
    public async Task ImmutableReadonlyConnection_RejectsWriteAndReplayPreservesDbWalAndShm()
    {
        await using var fixture = await Fixture.CreatePopulatedAsync();
        var before = await fixture.SnapshotAsync();
        var connectionString = HorseIdentityMigrationReadonlyReplay.CreateImmutableReadonlyConnectionString(
            fixture.EventStorePath);
        Assert.IsTrue(connectionString.Contains("immutable=1", StringComparison.OrdinalIgnoreCase));

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE EventEntity SET Data = Data WHERE AggregateId = '{Fixture.HorseId1}';";
            await Assert.ThrowsExactlyAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        }

        await HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            fixture.EventStorePath, fixture.Targets, CancellationToken.None);

        Assert.AreEqual(before, await fixture.SnapshotAsync());
    }

    private static HorseIdentityMigrationReplayTarget Target(
        HorseIdentityMigrationReplayAggregateKind kind, string aggregateId, int version) => new(kind, aggregateId, version);

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertFailsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (AssertFailedException)
        {
            throw;
        }
        catch (Exception)
        {
            return;
        }

        Assert.Fail("Expected the malformed stream to fail closed.");
    }

    private static void AssertCanRenameAfterReplay(string path)
    {
        var renamedPath = path + ".released";
        File.Move(path, renamedPath);
        File.Move(renamedPath, path);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string HorseId1 = "horse-00000000-0000-4000-8000-000000000101";
        internal const string HorseId2 = "horse-00000000-0000-4000-8000-000000000102";
        internal const string RaceId1 = "race-00000000-0000-4000-8000-000000000101";
        internal const string RaceId2 = "race-00000000-0000-4000-8000-000000000102";
        private const string MemoId1 = "memo-00000000-0000-4000-8000-000000000101";
        private const string MemoId2 = "memo-00000000-0000-4000-8000-000000000102";
        private const string TicketId1 = "predictionticket-00000000-0000-4000-8000-000000000101";
        private const string TicketId2 = "predictionticket-00000000-0000-4000-8000-000000000102";
        private readonly string _root;

        private Fixture(string root)
        {
            _root = root;
            EventStorePath = Path.Combine(root, "eventstore.db");
        }

        internal string EventStorePath { get; }
        internal IReadOnlyList<HorseIdentityMigrationReplayTarget> Targets =>
        [
            Target(HorseIdentityMigrationReplayAggregateKind.Horse, HorseId1, 1),
            Target(HorseIdentityMigrationReplayAggregateKind.Horse, HorseId2, 1),
            Target(HorseIdentityMigrationReplayAggregateKind.Race, RaceId1, 2),
            Target(HorseIdentityMigrationReplayAggregateKind.Race, RaceId2, 2),
            Target(HorseIdentityMigrationReplayAggregateKind.Memo, MemoId1, 2),
            Target(HorseIdentityMigrationReplayAggregateKind.Memo, MemoId2, 2),
            Target(HorseIdentityMigrationReplayAggregateKind.PredictionTicket, TicketId1, 2),
            Target(HorseIdentityMigrationReplayAggregateKind.PredictionTicket, TicketId2, 2),
        ];

        internal string SourceSnapshot { get; private set; } = string.Empty;

        internal static async Task<Fixture> CreatePopulatedAsync()
        {
            var fixture = new Fixture(Path.Combine(Path.GetTempPath(), "hrp-readonly-replay", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixture._root);

            var writableProvider = new SqliteDbContextProvider($"Data Source={fixture.EventStorePath};Pooling=False");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(writableProvider);
            services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(
                provider => provider.GetRequiredService<SqliteDbContextProvider>());
            services.AddEventFlow(options => options
                .AddDefaults(typeof(HorseAggregate).Assembly)
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .AddDefaults(typeof(CreateRaceCommand).Assembly)
                .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
                .UseEntityFrameworkEventStore<EventStoreDbContext>());

            try
            {
                await using (var context = writableProvider.CreateContext())
                    await context.Database.EnsureCreatedAsync();

                using (var provider = services.BuildServiceProvider())
                {
                    var commandBus = provider.GetRequiredService<ICommandBus>();
                    await SeedAsync(commandBus);
                }
            }
            finally
            {
                writableProvider.Dispose();
            }

            fixture.SourceSnapshot = await fixture.SnapshotAsync();
            return fixture;
        }

        internal static async Task<Fixture> CreateEmptyAsync()
        {
            var fixture = new Fixture(Path.Combine(Path.GetTempPath(), "hrp-readonly-replay", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixture._root);
            using (var provider = new SqliteDbContextProvider($"Data Source={fixture.EventStorePath};Pooling=False"))
            await using (var context = provider.CreateContext())
                await context.Database.EnsureCreatedAsync();
            fixture.SourceSnapshot = await fixture.SnapshotAsync();
            return fixture;
        }

        internal string CopyForMutation(string name)
        {
            var copy = Path.Combine(_root, $"{name}.db");
            File.Copy(EventStorePath, copy);
            return copy;
        }

        internal async Task<string> SnapshotAsync()
        {
            var builder = new StringBuilder();
            foreach (var path in new[] { EventStorePath, EventStorePath + "-wal", EventStorePath + "-shm" })
            {
                builder.Append(Path.GetFileName(path)).Append(':');
                if (!File.Exists(path))
                {
                    builder.AppendLine("absent");
                    continue;
                }
                builder.Append(Convert.ToHexString(await File.ReadAllBytesAsync(path))).AppendLine();
            }
            return builder.ToString();
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static async Task SeedAsync(ICommandBus commandBus)
        {
            foreach (var horseId in new[] { HorseId1, HorseId2 })
                await commandBus.PublishAsync(new RegisterHorseCommand(new HorseId(horseId), "Fixture Horse", "FixtureHorse"),
                    CancellationToken.None);

            foreach (var raceId in new[] { RaceId1, RaceId2 })
            {
                var id = new RaceId(raceId);
                await commandBus.PublishAsync(new CreateRaceCommand(id,
                    new DateOnly(2026, 10, 14), "TOKYO", 1, "Fixture Race"), CancellationToken.None);
                await commandBus.PublishAsync(new PublishRaceCardCommand(id, 0), CancellationToken.None);
            }

            foreach (var memoId in new[] { MemoId1, MemoId2 })
            {
                var id = new MemoId(memoId);
                await commandBus.PublishAsync(new CreateMemoCommand(id, "fixture", "Note", "before",
                    DateTimeOffset.Parse("2026-10-14T00:00:00Z"),
                    [new(MemoSubjectType.Horse, HorseId1)]), CancellationToken.None);
                await commandBus.PublishAsync(new UpdateMemoCommand(id, content: "after"), CancellationToken.None);
            }

            foreach (var ticketId in new[] { TicketId1, TicketId2 })
            {
                var id = new PredictionTicketId(ticketId);
                await commandBus.PublishAsync(new CreatePredictionTicketCommand(id, RaceId1, "fixture", "model",
                    .5m, "summary"), CancellationToken.None);
                await commandBus.PublishAsync(new AddPredictionMarkCommand(id, "entry-1", "A", 1, 1m, null),
                    CancellationToken.None);
            }
        }
    }
}
