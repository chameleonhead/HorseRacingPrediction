using System.Diagnostics;
using System.Text;
using EventFlow;
using EventFlow.Aggregates;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum HorseIdentityMigrationReplayAggregateKind
{
    Horse,
    Race,
    Memo,
    PredictionTicket,
}

internal sealed record HorseIdentityMigrationReplayTarget(
    HorseIdentityMigrationReplayAggregateKind Kind,
    string AggregateId,
    int ExpectedVersion);

/// <summary>Proves bounded EventFlow replay against an existing immutable SQLite file.</summary>
internal static class HorseIdentityMigrationReadonlyReplay
{
    private const int MaximumRows = 1_000_000;
    private const int MaximumAggregateIdBytes = 512;
    private const long MaximumInputBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumEventFieldBytes = 64 * 1024;
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(30);

    internal static async Task VerifyAsync(
        string trustedEventStorePath,
        IReadOnlyCollection<HorseIdentityMigrationReplayTarget> expectedTargets,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedEventStorePath);
        ArgumentNullException.ThrowIfNull(expectedTargets);

        var targets = ValidateTargets(expectedTargets);
        var fullPath = Path.GetFullPath(trustedEventStorePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The trusted EventStore file does not exist.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MaximumDuration);
        var startedAt = Stopwatch.StartNew();
        var actual = await ReadSupportedRootsAsync(fullPath, startedAt, timeout.Token).ConfigureAwait(false);
        EnsureSameMembership(targets, actual);

        using var services = BuildReadonlyEventFlowServices(fullPath);
        var aggregateStore = services.GetRequiredService<IAggregateStore>();
        foreach (var target in targets.Values.OrderBy(target => target.Kind)
                     .ThenBy(target => target.AggregateId, StringComparer.Ordinal))
        {
            timeout.Token.ThrowIfCancellationRequested();
            EnsureWithinBounds(startedAt, timeout.Token);
            var version = await LoadVersionAsync(aggregateStore, target, timeout.Token).ConfigureAwait(false);
            if (version != target.ExpectedVersion)
                throw InvalidStream();
        }
    }

    internal static string CreateImmutableReadonlyConnectionString(string trustedEventStorePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedEventStorePath);
        var fullPath = Path.GetFullPath(trustedEventStorePath);
        var fileUri = new Uri(fullPath, UriKind.Absolute).AbsoluteUri;
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fileUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 30,
        };
        return builder.ConnectionString;
    }

    private static Dictionary<(HorseIdentityMigrationReplayAggregateKind Kind, string AggregateId),
        HorseIdentityMigrationReplayTarget> ValidateTargets(
        IReadOnlyCollection<HorseIdentityMigrationReplayTarget> expectedTargets)
    {
        if (expectedTargets.Count > MaximumRows)
            throw InvalidStream();

        var targets = new Dictionary<(HorseIdentityMigrationReplayAggregateKind, string),
            HorseIdentityMigrationReplayTarget>();
        long identifierBytes = 0;
        foreach (var target in expectedTargets)
        {
            if (target is null || !Enum.IsDefined(target.Kind) || target.ExpectedVersion <= 0
                || target.ExpectedVersion > MaximumRows || string.IsNullOrWhiteSpace(target.AggregateId))
                throw InvalidStream();
            identifierBytes = checked(identifierBytes + Encoding.UTF8.GetByteCount(target.AggregateId));
            if (identifierBytes > HorseIdentityMigrationPreview.MaximumSummaryBytes)
                throw InvalidStream();
            if (!targets.TryAdd((target.Kind, target.AggregateId), target))
                throw InvalidStream();
        }

        return targets;
    }

    private static async Task<Dictionary<(HorseIdentityMigrationReplayAggregateKind Kind, string AggregateId), int>>
        ReadSupportedRootsAsync(string eventStorePath, Stopwatch startedAt, CancellationToken cancellationToken)
    {
        var names = SupportedAggregateNames();
        var byName = names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var actual = new Dictionary<(HorseIdentityMigrationReplayAggregateKind, string), int>();
        var globalSequences = new HashSet<long>();
        long rows = 0;
        long inputBytes = 0;
        long aggregateIdBytes = 0;

        await using var connection = new SqliteConnection(CreateImmutableReadonlyConnectionString(eventStorePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT AggregateName,
                   CASE WHEN length(CAST(AggregateId AS BLOB)) <= $maxAggregateIdBytes
                        THEN AggregateId ELSE NULL END,
                   AggregateSequenceNumber, GlobalSequenceNumber,
                   length(CAST(Data AS BLOB)), length(CAST(Metadata AS BLOB)),
                   Data IS NULL, Metadata IS NULL
            FROM EventEntity
            WHERE AggregateName IN ($horse, $race, $memo, $ticket)
            ORDER BY AggregateName, AggregateId, AggregateSequenceNumber;
            """;
        command.Parameters.AddWithValue("$horse", names[HorseIdentityMigrationReplayAggregateKind.Horse]);
        command.Parameters.AddWithValue("$race", names[HorseIdentityMigrationReplayAggregateKind.Race]);
        command.Parameters.AddWithValue("$memo", names[HorseIdentityMigrationReplayAggregateKind.Memo]);
        command.Parameters.AddWithValue("$ticket", names[HorseIdentityMigrationReplayAggregateKind.PredictionTicket]);
        command.Parameters.AddWithValue("$maxAggregateIdBytes", MaximumAggregateIdBytes);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        (HorseIdentityMigrationReplayAggregateKind Kind, string AggregateId)? current = null;
        var previousSequence = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            EnsureWithinBounds(startedAt, cancellationToken);
            rows++;
            if (rows > MaximumRows) throw InvalidStream();

            if (reader.IsDBNull(1)) throw InvalidStream();

            var aggregateName = reader.GetString(0);
            var aggregateId = reader.GetString(1);
            var sequence = reader.GetInt32(2);
            var globalSequence = reader.GetInt64(3);
            if (!byName.TryGetValue(aggregateName, out var kind)
                || reader.IsDBNull(1) || string.IsNullOrWhiteSpace(aggregateId)
                || sequence <= 0 || globalSequence <= 0 || reader.GetBoolean(6) || reader.GetBoolean(7))
                throw InvalidStream();

            var dataBytes = reader.GetInt64(4);
            var metadataBytes = reader.GetInt64(5);
            if (dataBytes < 0 || dataBytes > MaximumEventFieldBytes
                || metadataBytes < 0 || metadataBytes > MaximumEventFieldBytes)
                throw InvalidStream();
            inputBytes = checked(inputBytes + dataBytes + metadataBytes);
            if (inputBytes > MaximumInputBytes) throw InvalidStream();
            if (!globalSequences.Add(globalSequence)) throw InvalidStream();

            var key = (kind, aggregateId);
            if (current != key)
            {
                if (actual.ContainsKey(key)) throw InvalidStream();
                aggregateIdBytes = checked(aggregateIdBytes + Encoding.UTF8.GetByteCount(aggregateId));
                if (aggregateIdBytes > HorseIdentityMigrationPreview.MaximumSummaryBytes)
                    throw InvalidStream();
                current = key;
                previousSequence = 0;
                actual.Add(key, 0);
            }
            if (sequence != previousSequence + 1) throw InvalidStream();
            previousSequence = sequence;
            actual[key] = sequence;
        }

        EnsureWithinBounds(startedAt, cancellationToken);
        return actual;
    }

    private static void EnsureSameMembership(
        IReadOnlyDictionary<(HorseIdentityMigrationReplayAggregateKind Kind, string AggregateId),
            HorseIdentityMigrationReplayTarget> expected,
        IReadOnlyDictionary<(HorseIdentityMigrationReplayAggregateKind Kind, string AggregateId), int> actual)
    {
        if (expected.Count != actual.Count)
            throw InvalidStream();
        foreach (var (key, target) in expected)
            if (!actual.TryGetValue(key, out var version) || version != target.ExpectedVersion)
                throw InvalidStream();
    }

    private static async Task<int> LoadVersionAsync(
        IAggregateStore aggregateStore,
        HorseIdentityMigrationReplayTarget target,
        CancellationToken cancellationToken)
    {
        return target.Kind switch
        {
            HorseIdentityMigrationReplayAggregateKind.Horse => (await aggregateStore
                .LoadAsync<HorseAggregate, HorseId>(new HorseId(target.AggregateId), cancellationToken)
                .ConfigureAwait(false)).Version,
            HorseIdentityMigrationReplayAggregateKind.Race => (await aggregateStore
                .LoadAsync<RaceAggregate, RaceId>(new RaceId(target.AggregateId), cancellationToken)
                .ConfigureAwait(false)).Version,
            HorseIdentityMigrationReplayAggregateKind.Memo => (await aggregateStore
                .LoadAsync<MemoAggregate, MemoId>(new MemoId(target.AggregateId), cancellationToken)
                .ConfigureAwait(false)).Version,
            HorseIdentityMigrationReplayAggregateKind.PredictionTicket => (await aggregateStore
                .LoadAsync<PredictionTicketAggregate, PredictionTicketId>(
                    new PredictionTicketId(target.AggregateId), cancellationToken).ConfigureAwait(false)).Version,
            _ => throw InvalidStream(),
        };
    }

    private static ServiceProvider BuildReadonlyEventFlowServices(string eventStorePath)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var connectionString = CreateImmutableReadonlyConnectionString(eventStorePath);
        services.AddSingleton(_ => new SqliteDbContextProvider(connectionString));
        services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(
            provider => provider.GetRequiredService<SqliteDbContextProvider>());
        services.AddEventFlow(options => options
            .AddDefaults(typeof(HorseAggregate).Assembly)
            .AddDefaults(typeof(RaceAggregate).Assembly)
            .AddDefaults(typeof(CreateRaceCommand).Assembly)
            .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
            .UseEntityFrameworkEventStore<EventStoreDbContext>());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IReadOnlyDictionary<HorseIdentityMigrationReplayAggregateKind, string>
        SupportedAggregateNames() => new Dictionary<HorseIdentityMigrationReplayAggregateKind, string>
        {
            [HorseIdentityMigrationReplayAggregateKind.Horse] = new HorseAggregate(HorseId.New).Name.Value,
            [HorseIdentityMigrationReplayAggregateKind.Race] = new RaceAggregate(RaceId.New).Name.Value,
            [HorseIdentityMigrationReplayAggregateKind.Memo] = new MemoAggregate(MemoId.New).Name.Value,
            [HorseIdentityMigrationReplayAggregateKind.PredictionTicket] =
                new PredictionTicketAggregate(PredictionTicketId.New).Name.Value,
        };

    private static void EnsureWithinBounds(Stopwatch startedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (startedAt.Elapsed > MaximumDuration) throw InvalidStream();
    }

    private static InvalidDataException InvalidStream() =>
        new("The configured EventStore replay evidence is incomplete or inconsistent.");
}
