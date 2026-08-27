using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class ProcessingStateDbContext : DbContext
{
    public ProcessingStateDbContext(DbContextOptions<ProcessingStateDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProcessingJobEntity> Jobs => Set<ProcessingJobEntity>();

    public DbSet<ProcessingMarkerEntity> Markers => Set<ProcessingMarkerEntity>();

    public DbSet<CollectionDispatchOutboxEntity> DispatchOutbox => Set<CollectionDispatchOutboxEntity>();

    public DbSet<RaceDataCollectionStatusEntity> RaceDataCollectionStatuses => Set<RaceDataCollectionStatusEntity>();

    public DbSet<AgentAcquisitionStatusEntity> AgentAcquisitionStatuses => Set<AgentAcquisitionStatusEntity>();

    public DbSet<ResultDayCollectionStatusEntity> ResultDayCollectionStatuses => Set<ResultDayCollectionStatusEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessingJobEntity>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(x => x.JobId);
            entity.Property(x => x.JobId).HasColumnName("job_id");
            entity.Property(x => x.JobType).HasColumnName("job_type");
            entity.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key");
            entity.Property(x => x.Payload).HasColumnName("payload");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(x => x.Priority).HasColumnName("priority");
            entity.Property(x => x.FirstQueuedAt).HasColumnName("first_queued_at");
            entity.Property(x => x.AvailableAt).HasColumnName("available_at");
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
            entity.Property(x => x.LeaseToken).HasColumnName("lease_token");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            entity.Property(x => x.LastError).HasColumnName("last_error");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(x => new { x.JobType, x.DeduplicationKey })
                .IsUnique();
            entity.HasIndex(x => new { x.JobType, x.Status, x.AvailableAt, x.FirstQueuedAt, x.Priority });
            entity.HasIndex(x => new { x.Status, x.LeaseExpiresAt });
        });

        modelBuilder.Entity<CollectionDispatchOutboxEntity>(entity =>
        {
            entity.ToTable("collection_dispatch_outbox");
            entity.HasKey(x => x.OutboxId);
            entity.Property(x => x.OutboxId).HasColumnName("outbox_id");
            entity.Property(x => x.TaskId).HasColumnName("task_id");
            entity.Property(x => x.JobType).HasColumnName("job_type");
            entity.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key");
            entity.Property(x => x.AvailableAt).HasColumnName("available_at");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            entity.Property(x => x.DispatchedAt).HasColumnName("dispatched_at");
            entity.Property(x => x.LastError).HasColumnName("last_error");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => new { x.DispatchedAt, x.AvailableAt });
        });

        modelBuilder.Entity<ProcessingMarkerEntity>(entity =>
        {
            entity.ToTable("markers");
            entity.HasKey(x => new { x.MarkerType, x.MarkerKey });
            entity.Property(x => x.MarkerType).HasColumnName("marker_type");
            entity.Property(x => x.MarkerKey).HasColumnName("marker_key");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<RaceDataCollectionStatusEntity>(entity =>
        {
            entity.ToTable("race_data_collection_statuses");
            entity.HasKey(x => x.RaceKey);
            entity.Property(x => x.RaceKey).HasColumnName("race_key");
            entity.Property(x => x.RaceDate).HasColumnName("race_date");
            entity.Property(x => x.Racecourse).HasColumnName("racecourse");
            entity.Property(x => x.RaceNumber).HasColumnName("race_number");
            entity.Property(x => x.RaceId).HasColumnName("race_id");
            entity.Property(x => x.RaceName).HasColumnName("race_name");
            entity.Property(x => x.RaceCardUrl).HasColumnName("race_card_url");
            entity.Property(x => x.RaceCardStatus).HasColumnName("race_card_status").HasConversion<string>();
            entity.Property(x => x.RaceCardErrorCode).HasColumnName("race_card_error_code").HasConversion<string>();
            entity.Property(x => x.RaceCardErrorReason).HasColumnName("race_card_error_reason");
            entity.Property(x => x.RaceCardUpdatedAt).HasColumnName("race_card_updated_at");
            entity.Property(x => x.RaceResultUrl).HasColumnName("race_result_url");
            entity.Property(x => x.RaceResultStatus).HasColumnName("race_result_status").HasConversion<string>();
            entity.Property(x => x.RaceResultOrigin).HasColumnName("race_result_origin").HasConversion<string>();
            entity.Property(x => x.RequestedByRaceId).HasColumnName("requested_by_race_id");
            entity.Property(x => x.RaceResultErrorCode).HasColumnName("race_result_error_code").HasConversion<string>();
            entity.Property(x => x.RaceResultErrorReason).HasColumnName("race_result_error_reason");
            entity.Property(x => x.RaceResultUpdatedAt).HasColumnName("race_result_updated_at");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(x => x.RaceDate);
            entity.HasIndex(x => new { x.RaceDate, x.Racecourse, x.RaceNumber });
        });

        modelBuilder.Entity<AgentAcquisitionStatusEntity>(entity =>
        {
            entity.ToTable("agent_acquisition_statuses");
            entity.HasKey(x => x.AcquisitionKey);
            entity.Property(x => x.AcquisitionKey).HasColumnName("acquisition_key");
            entity.Property(x => x.SubjectType).HasColumnName("subject_type").HasConversion<string>();
            entity.Property(x => x.OperationType).HasColumnName("operation_type").HasConversion<string>();
            entity.Property(x => x.ProviderType).HasColumnName("provider_type");
            entity.Property(x => x.SubjectId).HasColumnName("subject_id");
            entity.Property(x => x.SubjectName).HasColumnName("subject_name");
            entity.Property(x => x.RelatedRaceId).HasColumnName("related_race_id");
            entity.Property(x => x.SourceUrl).HasColumnName("source_url");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasConversion<string>();
            entity.Property(x => x.ErrorReason).HasColumnName("error_reason");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(x => x.UpdatedAt);
            entity.HasIndex(x => new { x.SubjectType, x.Status, x.UpdatedAt });
        });

        modelBuilder.Entity<ResultDayCollectionStatusEntity>(entity =>
        {
            entity.ToTable("result_day_collection_statuses");
            entity.HasKey(x => x.DayKey);
            entity.Property(x => x.DayKey).HasColumnName("day_key");
            entity.Property(x => x.ProviderType).HasColumnName("provider_type");
            entity.Property(x => x.TargetYear).HasColumnName("target_year");
            entity.Property(x => x.TargetMonth).HasColumnName("target_month");
            entity.Property(x => x.TargetDate).HasColumnName("target_date");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(x => x.ExpectedRaceCount).HasColumnName("expected_race_count");
            entity.Property(x => x.CompletedRaceCount).HasColumnName("completed_race_count");
            entity.Property(x => x.IncompleteReason).HasColumnName("incomplete_reason");
            entity.Property(x => x.LastCompletedAt).HasColumnName("last_completed_at");
            entity.Property(x => x.RetryAfter).HasColumnName("retry_after");
            entity.Property(x => x.LastError).HasColumnName("last_error");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(x => x.TargetDate);
            entity.HasIndex(x => new { x.ProviderType, x.TargetYear, x.TargetMonth, x.TargetDate });
            entity.HasIndex(x => new { x.ProviderType, x.Status, x.TargetDate });
        });
    }
}
