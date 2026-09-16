using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.CollectionOperations.Persistence;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class CollectionPlatformDbContext(DbContextOptions<CollectionPlatformDbContext> options) : DbContext(options)
{
    public DbSet<CollectionResourceEntity> Resources => Set<CollectionResourceEntity>();
    public DbSet<CollectionResourceSuppressionEntity> ResourceSuppressions => Set<CollectionResourceSuppressionEntity>();
    public DbSet<CollectionDefinitionEntity> Definitions => Set<CollectionDefinitionEntity>();
    public DbSet<CollectionRevisionEntity> Revisions => Set<CollectionRevisionEntity>();
    public DbSet<CollectionRevisionImpactEntity> RevisionImpacts => Set<CollectionRevisionImpactEntity>();
    public DbSet<CollectionStateEntity> States => Set<CollectionStateEntity>();
    public DbSet<CollectionRequestEntity> Requests => Set<CollectionRequestEntity>();
    public DbSet<CollectionRequestBatchBindingEntity> RequestBatchBindings => Set<CollectionRequestBatchBindingEntity>();
    public DbSet<CollectionTaskEntity> Tasks => Set<CollectionTaskEntity>();
    public DbSet<CollectionActiveTaskEntity> ActiveTasks => Set<CollectionActiveTaskEntity>();
    public DbSet<CollectionAttemptEntity> Attempts => Set<CollectionAttemptEntity>();
    public DbSet<ResourceLocationEntity> Locations => Set<ResourceLocationEntity>();
    public DbSet<CollectionDispatchOutboxEntity> DispatchOutbox => Set<CollectionDispatchOutboxEntity>();
    public DbSet<CollectionExecutionLeaseEntity> ExecutionLeases => Set<CollectionExecutionLeaseEntity>();
    public DbSet<CollectionPlatformControlEntity> Controls => Set<CollectionPlatformControlEntity>();
    public DbSet<CollectionFailureNotificationEntity> FailureNotifications => Set<CollectionFailureNotificationEntity>();
    public DbSet<BackfillBatchEntity> BackfillBatches => Set<BackfillBatchEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<JstDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableJstDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionResourceEntity>(e =>
        {
            e.ToTable("collection_resources"); e.HasKey(x => x.ResourcePk);
            e.HasIndex(x => new { x.Type, x.Provider, x.ResourceId }).IsUnique();
            e.Property(x => x.Type).HasConversion<string>();
        });
        modelBuilder.Entity<CollectionResourceSuppressionEntity>(e =>
        {
            e.ToTable("collection_resource_suppressions");
            e.HasKey(x => new { x.Type, x.Provider, x.ResourceId });
            e.Property(x => x.Type).HasConversion<string>();
            e.HasIndex(x => x.RepairId);
        });
        modelBuilder.Entity<CollectionDefinitionEntity>(e =>
        {
            e.ToTable("collection_definitions"); e.HasKey(x => x.DefinitionId);
            e.Property(x => x.ResourceType).HasConversion<string>();
        });
        modelBuilder.Entity<CollectionRevisionEntity>(e =>
        {
            e.ToTable("collection_revisions"); e.HasKey(x => new { x.DefinitionId, x.Revision });
        });
        modelBuilder.Entity<CollectionRevisionImpactEntity>(e =>
        {
            e.ToTable("collection_revision_impacts"); e.HasKey(x => x.ImpactId);
            e.Property(x => x.ScopeType).HasConversion<string>();
            e.HasIndex(x => new { x.DefinitionId, x.Revision });
        });
        modelBuilder.Entity<CollectionStateEntity>(e =>
        {
            e.ToTable("collection_states"); e.HasKey(x => new { x.ResourcePk, x.DefinitionId });
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.Status, x.NextCollectionAt });
        });
        modelBuilder.Entity<CollectionRequestEntity>(e =>
        {
            e.ToTable("collection_requests"); e.HasKey(x => x.RequestId);
            e.Property(x => x.Reason).HasConversion<string>();
            e.HasIndex(x => new { x.ResourcePk, x.DefinitionId, x.RequestedAt });
        });
        modelBuilder.Entity<CollectionRequestBatchBindingEntity>(e =>
        {
            e.ToTable("collection_request_batch_bindings"); e.HasKey(x => x.BatchItemId);
            e.HasIndex(x => x.RequestId);
        });
        modelBuilder.Entity<CollectionTaskEntity>(e =>
        {
            e.ToTable("collection_tasks"); e.HasKey(x => x.TaskId);
            e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.Lane).HasConversion<string>();
            e.HasIndex(x => new { x.Status, x.AvailableAt, x.Lane, x.Priority });
            e.HasIndex(x => new { x.ResourcePk, x.DefinitionId, x.RequestedRevision, x.CreatedAt });
            e.HasIndex(x => x.RequestId);
        });
        modelBuilder.Entity<CollectionActiveTaskEntity>(e =>
        {
            e.ToTable("collection_active_tasks"); e.HasKey(x => new { x.ResourcePk, x.DefinitionId });
            e.HasIndex(x => x.TaskId).IsUnique();
        });
        modelBuilder.Entity<CollectionAttemptEntity>(e =>
        {
            e.ToTable("collection_attempts"); e.HasKey(x => x.AttemptId);
            e.Property(x => x.Result).HasConversion<string>();
            e.HasIndex(x => new { x.TaskId, x.AttemptNumber }).IsUnique();
            e.HasIndex(x => x.ExecutionBatchId);
        });
        modelBuilder.Entity<ResourceLocationEntity>(e =>
        {
            e.ToTable("resource_locations"); e.HasKey(x => x.LocationId);
            e.Property(x => x.Source).HasConversion<string>(); e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.ResourcePk, x.DefinitionId, x.Url }).IsUnique();
        });
        modelBuilder.Entity<CollectionDispatchOutboxEntity>(e =>
        {
            e.ToTable("collection_task_outbox"); e.HasKey(x => x.OutboxId);
            e.HasIndex(x => new { x.DispatchedAt, x.AvailableAt });
            e.HasIndex(x => new { x.DispatchedAt, x.ReservedUntilUnixMilliseconds });
        });
        modelBuilder.Entity<CollectionExecutionLeaseEntity>(e =>
        {
            e.ToTable("collection_execution_leases"); e.HasKey(x => x.ExecutionBatchId);
            e.HasIndex(x => x.DispatchEnvelopeId).IsUnique();
            e.HasIndex(x => new { x.Status, x.LeaseExpiresAt });
        });
        modelBuilder.Entity<CollectionPlatformControlEntity>(e =>
        {
            e.ToTable("collection_platform_controls"); e.HasKey(x => x.ControlId);
        });
        modelBuilder.Entity<CollectionFailureNotificationEntity>(e =>
        {
            e.ToTable("collection_failure_notifications"); e.HasKey(x => x.NotificationId);
            e.HasIndex(x => new { x.PublishedAt, x.AvailableAt });
            e.Property(x => x.ResolutionStatus).HasConversion<string>();
            e.HasIndex(x => new { x.ResolutionStatus, x.AvailableAt });
            e.HasIndex(x => x.RecoveryTaskId);
            e.HasIndex(x => x.TaskId);
        });
        modelBuilder.Entity<BackfillBatchEntity>(e =>
        {
            e.ToTable("collection_backfill_batches"); e.HasKey(x => x.BatchId);
            e.HasIndex(x => new { x.Provider, x.From, x.To });
        });
    }
}
