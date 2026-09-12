using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.PredictionScheduling;

public sealed class PredictionScheduleStore : IPredictionSchedule
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DbContextOptions<PredictionScheduleDbContext> _dbOptions;

    public PredictionScheduleStore(IOptions<PredictionScheduleOptions> options)
    {
        var configured = options.Value;
        var directory = string.IsNullOrWhiteSpace(configured.StateDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "prediction-scheduling-state")
            : configured.StateDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, string.IsNullOrWhiteSpace(configured.StoreFileName)
            ? "prediction-executions.db" : configured.StoreFileName);
        _dbOptions = new DbContextOptionsBuilder<PredictionScheduleDbContext>()
            .UseSqlite($"Data Source={Path.GetFullPath(path)};Pooling=False")
            .Options;
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public async Task EnqueueAsync(IEnumerable<string> raceIds, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDb();
            foreach (var raceId in raceIds.Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.Ordinal))
            {
                var entity = await db.Candidates.FindAsync([raceId], cancellationToken).ConfigureAwait(false);
                if (entity is null)
                {
                    db.Candidates.Add(new PredictionCandidateEntity
                    {
                        RaceId = raceId,
                        Status = PredictionCandidateStatus.Ready,
                        FirstQueuedAt = now,
                        AvailableAt = now,
                        UpdatedAt = now,
                    });
                }
                else if (entity.Status != PredictionCandidateStatus.Running)
                {
                    entity.Status = PredictionCandidateStatus.Ready;
                    entity.FirstQueuedAt = now;
                    entity.AvailableAt = now;
                    entity.LeaseToken = null;
                    entity.LeaseExpiresAt = null;
                    entity.LastError = null;
                    entity.UpdatedAt = now;
                }
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<PredictionCandidateLease>> AcquireAsync(DateTimeOffset now, TimeSpan minAge,
        int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDb();
            var all = await db.Candidates.ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var expired in all.Where(x => x.Status == PredictionCandidateStatus.Running
                                                   && x.LeaseExpiresAt <= now))
            {
                expired.Status = PredictionCandidateStatus.Ready;
                expired.LeaseToken = null;
                expired.LeaseExpiresAt = null;
                expired.AvailableAt = now;
                expired.UpdatedAt = now;
            }

            var ready = all.Where(x => x.Status == PredictionCandidateStatus.Ready
                                       && x.AvailableAt <= now
                                       && x.FirstQueuedAt <= now.Subtract(minAge))
                .OrderBy(x => x.AvailableAt).ThenBy(x => x.FirstQueuedAt)
                .Take(Math.Max(1, maxCount)).ToList();
            var leases = new List<PredictionCandidateLease>(ready.Count);
            foreach (var candidate in ready)
            {
                var token = Guid.NewGuid().ToString("N");
                candidate.Status = PredictionCandidateStatus.Running;
                candidate.LeaseToken = token;
                candidate.LeaseExpiresAt = now.Add(leaseDuration);
                candidate.UpdatedAt = now;
                leases.Add(new(candidate.RaceId, token));
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return leases;
        }
        finally { _gate.Release(); }
    }

    public Task<bool> CompleteAsync(string raceId, string leaseToken, CancellationToken cancellationToken = default)
        => UpdateLeaseAsync(raceId, leaseToken, PredictionCandidateStatus.Succeeded, null, null, cancellationToken);

    public Task<bool> RequeueAsync(string raceId, string leaseToken, DateTimeOffset availableAt, string? error,
        CancellationToken cancellationToken = default)
        => UpdateLeaseAsync(raceId, leaseToken, PredictionCandidateStatus.Ready, availableAt, error, cancellationToken);

    private async Task<bool> UpdateLeaseAsync(string raceId, string leaseToken, PredictionCandidateStatus status,
        DateTimeOffset? availableAt, string? error, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDb();
            var entity = await db.Candidates.FindAsync([raceId], cancellationToken).ConfigureAwait(false);
            if (entity is null || entity.Status != PredictionCandidateStatus.Running
                               || !string.Equals(entity.LeaseToken, leaseToken, StringComparison.Ordinal)) return false;
            entity.Status = status;
            if (availableAt is not null) entity.AvailableAt = availableAt.Value;
            entity.LeaseToken = null;
            entity.LeaseExpiresAt = null;
            entity.LastError = error;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private PredictionScheduleDbContext CreateDb() => new(_dbOptions);
}

internal enum PredictionCandidateStatus { Ready, Running, Succeeded }

internal sealed class PredictionCandidateEntity
{
    public required string RaceId { get; set; }
    public PredictionCandidateStatus Status { get; set; }
    public DateTimeOffset FirstQueuedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class PredictionScheduleDbContext(DbContextOptions<PredictionScheduleDbContext> options)
    : DbContext(options)
{
    public DbSet<PredictionCandidateEntity> Candidates => Set<PredictionCandidateEntity>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PredictionCandidateEntity>(entity =>
        {
            entity.ToTable("prediction_candidates");
            entity.HasKey(x => x.RaceId);
            entity.Property(x => x.RaceId).HasMaxLength(128);
            entity.Property(x => x.LeaseToken).HasMaxLength(64);
            entity.HasIndex(x => new { x.Status, x.AvailableAt });
        });
    }
}
