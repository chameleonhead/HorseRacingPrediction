using EventFlow.EntityFramework.Extensions;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Linq.Expressions;
using System.Text.Json;

namespace HorseRacingPrediction.Infrastructure.Persistence;

public class EventStoreDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DbSet<HorseReadModel> Horses => Set<HorseReadModel>();
    public DbSet<JockeyReadModel> Jockeys => Set<JockeyReadModel>();
    public DbSet<TrainerReadModel> Trainers => Set<TrainerReadModel>();
    public DbSet<RacePredictionContextReadModel> RacePredictionContexts => Set<RacePredictionContextReadModel>();
    public DbSet<RaceResultViewReadModel> RaceResults => Set<RaceResultViewReadModel>();
    public DbSet<PredictionTicketReadModel> PredictionTickets => Set<PredictionTicketReadModel>();
    public DbSet<HorseWeightHistoryReadModel> HorseWeightHistories => Set<HorseWeightHistoryReadModel>();
    public DbSet<PredictionComparisonViewReadModel> PredictionComparisons => Set<PredictionComparisonViewReadModel>();
    public DbSet<MemoBySubjectReadModel> MemoSubjects => Set<MemoBySubjectReadModel>();
    public DbSet<HorseRaceHistoryReadModel> HorseRaceHistories => Set<HorseRaceHistoryReadModel>();
    public DbSet<JockeyRaceHistoryReadModel> JockeyRaceHistories => Set<JockeyRaceHistoryReadModel>();
    public DbSet<RaceSummaryReadModel> RaceSummaries => Set<RaceSummaryReadModel>();
    public DbSet<OwnerAliasMappingReadModel> OwnerAliasMappings => Set<OwnerAliasMappingReadModel>();
    public DbSet<OwnerMergeAuditReadModel> OwnerMergeAudits => Set<OwnerMergeAuditReadModel>();

    public EventStoreDbContext(DbContextOptions<EventStoreDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddEventFlowEvents();
        modelBuilder.AddEventFlowSnapshots();

        modelBuilder.Entity<RaceSummaryReadModel>(entity =>
        {
            entity.HasKey(x => x.RaceId);
        });

        modelBuilder.Entity<OwnerAliasMappingReadModel>(entity =>
        {
            entity.HasKey(x => x.NormalizedAlias);
            entity.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<OwnerMergeAuditReadModel>(entity =>
        {
            entity.HasKey(x => x.AuditId);
            entity.HasIndex(x => new { x.TargetOwnerId, x.CreatedAt });
        });

        modelBuilder.Entity<HorseReadModel>(entity =>
        {
            entity.HasKey(x => x.HorseId);
            ConfigureJsonProperty(entity, x => x.Aliases);
        });

        modelBuilder.Entity<JockeyReadModel>(entity =>
        {
            entity.HasKey(x => x.JockeyId);
            ConfigureJsonProperty(entity, x => x.Aliases);
        });

        modelBuilder.Entity<TrainerReadModel>(entity =>
        {
            entity.HasKey(x => x.TrainerId);
            ConfigureJsonProperty(entity, x => x.Aliases);
        });

        modelBuilder.Entity<RacePredictionContextReadModel>(entity =>
        {
            entity.HasKey(x => x.RaceId);
            entity.Ignore(x => x.LatestWeather);
            entity.Ignore(x => x.LatestTrackCondition);
            ConfigureJsonProperty(entity, x => x.Entries);
            ConfigureJsonProperty(entity, x => x.WeatherObservations);
            ConfigureJsonProperty(entity, x => x.TrackConditionObservations);
        });

        modelBuilder.Entity<RaceResultViewReadModel>(entity =>
        {
            entity.HasKey(x => x.RaceId);
            ConfigureJsonProperty(entity, x => x.EntryResults);
            ConfigureJsonProperty(entity, x => x.EntryIndexes);
            ConfigureJsonProperty(entity, x => x.PayoutResult);
        });

        modelBuilder.Entity<PredictionTicketReadModel>(entity =>
        {
            entity.HasKey(x => x.PredictionTicketId);
            entity.Ignore(x => x.LatestEvaluation);
            ConfigureJsonProperty(entity, x => x.Marks);
            ConfigureJsonProperty(entity, x => x.Evaluations);
        });

        modelBuilder.Entity<HorseWeightHistoryReadModel>(entity =>
        {
            entity.HasKey(x => x.HorseId);
            ConfigureJsonProperty(entity, x => x.WeightHistory);
        });

        modelBuilder.Entity<PredictionComparisonViewReadModel>(entity =>
        {
            entity.HasKey(x => x.RaceId);
            entity.Ignore(x => x.PredictionTickets);
            ConfigureJsonProperty(entity, x => x.TicketStates);
            ConfigureJsonProperty(entity, x => x.EntryIndexes);
            ConfigureJsonProperty(entity, x => x.EntryResults);
            ConfigureJsonProperty(entity, x => x.PayoutResult);
        });

        modelBuilder.Entity<MemoBySubjectReadModel>(entity =>
        {
            entity.HasKey(x => x.SubjectKey);
            ConfigureJsonProperty(entity, x => x.Memos);
        });

        modelBuilder.Entity<HorseRaceHistoryReadModel>(entity =>
        {
            entity.HasKey(x => x.HorseId);
            entity.Ignore(x => x.TotalRaceCount);
            entity.Ignore(x => x.WinCount);
            entity.Ignore(x => x.PlaceCount);
            entity.Ignore(x => x.WinRate);
            entity.Ignore(x => x.PlaceRate);
            entity.Ignore(x => x.RecentAvgFinishPosition);
            entity.Ignore(x => x.AvgLastThreeFurlongTime);
            entity.Ignore(x => x.AvgPrizeMoney);
            entity.Ignore(x => x.WeightStabilityScore);
            entity.Ignore(x => x.LatestRaceDate);
            entity.Ignore(x => x.LatestJockeyId);
            ConfigureJsonProperty(entity, x => x.Entries);
        });

        modelBuilder.Entity<JockeyRaceHistoryReadModel>(entity =>
        {
            entity.HasKey(x => x.JockeyId);
            entity.Ignore(x => x.TotalRaceCount);
            entity.Ignore(x => x.WinCount);
            entity.Ignore(x => x.PlaceCount);
            entity.Ignore(x => x.WinRate);
            entity.Ignore(x => x.PlaceRate);
            entity.Ignore(x => x.RecentWinRate);
            entity.Ignore(x => x.RecentPlaceRate);
            ConfigureJsonProperty(entity, x => x.Entries);
        });
    }

    private static void ConfigureJsonProperty<TEntity, TProperty>(
        EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TProperty>> propertyExpression)
        where TEntity : class
    {
        var converter = new ValueConverter<TProperty, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => JsonSerializer.Deserialize<TProperty>(value, JsonOptions)!);

        var comparer = new ValueComparer<TProperty>(
            (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(),
            value => JsonSerializer.Deserialize<TProperty>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!);

        var property = entity.Property(propertyExpression);
        property.HasConversion(converter);
        property.Metadata.SetValueComparer(comparer);
    }
}
