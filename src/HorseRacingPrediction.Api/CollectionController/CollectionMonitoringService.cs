using System.Security.Cryptography;
using System.Text;
using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionMonitoringOptions
{
    public const string SectionName = "CollectionMonitoring";
    public bool Enabled { get; set; } = true;
    public bool ChangeRecordEnabled { get; set; } = true;
    public bool RecoveryEnabled { get; set; }
    public bool MaintenanceMode { get; set; }
    public int MaxRows { get; set; } = 5_000;
    public int MaxFindings { get; set; } = 200;
    public int RunningStallMinutes { get; set; } = 30;
    public int DueStallMinutes { get; set; } = 60;
    public int RetryWaitingHours { get; set; } = 24;
    public int UnexpectedPauseMinutes { get; set; } = 30;
    public int DispatchLookbackHours { get; set; } = 24;
    public int CanaryLimit { get; set; } = 5;
    public int FridayCardCheckpointHour { get; set; } = 18;
    public int FridayCriticalHour { get; set; } = 21;
    public int ResultGraceMinutes { get; set; } = 30;
    public int RaceDayResultCheckpointHour { get; set; } = 18;
    public int RaceDayResultCheckpointMinute { get; set; } = 30;
    public string ClassifierVersion { get; set; } = "1";
}

public enum CollectionFindingClassification
{
    ProgramBug,
    KnownHistoricalJobError,
    UnknownHistoricalJobError,
    OperationalCondition,
}

public sealed record CollectionOperationalFinding(
    string Fingerprint,
    string Kind,
    CollectionFindingClassification Classification,
    string Severity,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    string Summary,
    IReadOnlyList<string> Evidence,
    string SuggestedScope,
    string? RecoveryRecipeId,
    string ClassifierVersion,
    string? RootCauseHypothesis = null,
    string? OwnerTask = null,
    string? NextSafeOperation = null);

public sealed record CollectionMonitoringReport(
    DateTimeOffset Cutoff,
    DateTimeOffset CompletedAt,
    bool Enabled,
    bool ChangeRecordEnabled,
    bool Suppressed,
    string? SuppressionReason,
    bool Truncated,
    IReadOnlyList<CollectionOperationalFinding> Findings,
    CollectionMonitoringOutcome Outcome = CollectionMonitoringOutcome.Healthy,
    IReadOnlyList<CollectionDefinitionFlowDiagnostic>? DefinitionFlows = null);

public sealed record CollectionDefinitionFlowDiagnostic(
    string Definition,
    string Lane,
    string CompatibilityKey,
    int Arrived,
    int Dispatched,
    int Completed,
    int Active,
    double OldestAgeMinutes,
    string Classification);

public enum CollectionMonitoringOutcome
{
    Healthy,
    FindingRecorded,
    ActionRequired,
    MonitorFailed,
}

public sealed record CollectionKnownRecoveryPreview(
    string RecipeId,
    int MatchingFindingCount,
    int CanaryLimit,
    bool RecoveryEnabled,
    bool MaintenanceMode,
    bool SafeToApply,
    string? BlockingReason);

public sealed record CollectionKnownRecoveryExecution(
    string RecipeId,
    string BackupId,
    string BackupFileName,
    int Examined,
    int Recovered,
    int Reused,
    int Suppressed,
    int Skipped,
    int Failed);

internal sealed record DomainRaceFreshness(DateOnly RaceDate, bool HasCard, bool HasResult);

public sealed record OwnerIdentityMigrationPreview(
    int DistinctOwnerNames,
    int CanonicalIds,
    int LegacyIds,
    int AliasMappedNames,
    IReadOnlyList<OwnerIdentityMigrationSample> Samples);

public sealed record OwnerIdentityMigrationSample(string DisplayName, string CanonicalId, string LegacyId,
    bool HasAliasMapping);

public sealed class CollectionMonitoringService(
    CollectionPlatformStore store,
    IOptions<CollectionMonitoringOptions> options,
    IDbContextProvider<EventStoreDbContext>? domainProvider = null)
{
    internal const string SubjectRecoveryRecipeId = "subject-identification-revision-gated-v1";
    private static readonly SemaphoreSlim RecoveryGate = new(1, 1);
    private readonly CollectionMonitoringOptions _options = options.Value;

    public async Task<CollectionMonitoringReport> InspectAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return new(now, now, false, _options.ChangeRecordEnabled, true,
                "Collection monitoring is disabled.", false, [], CollectionMonitoringOutcome.Healthy);

        var snapshot = await store.GetMonitoringSnapshotAsync(now,
            now.AddHours(-Math.Clamp(_options.DispatchLookbackHours, 1, 168)),
            _options.MaxRows, cancellationToken).ConfigureAwait(false);
        var maintenanceReason = GetMaintenanceReason(snapshot.Pipeline);
        if (maintenanceReason is not null)
            return new(now, DateTimeOffset.UtcNow, true, _options.ChangeRecordEnabled, true,
                maintenanceReason, snapshot.Truncated, [], CollectionMonitoringOutcome.Healthy);

        var findings = new List<CollectionOperationalFinding>();
        var failures = await store.GetActionableFailureNotificationsAsync(now,
            Math.Clamp(_options.MaxRows, 1, 10_000), cancellationToken).ConfigureAwait(false);
        foreach (var group in CollectionFailureGrouping.Build(failures))
        {
            var classification = ClassifyFailure(group);
            var recipe = classification == CollectionFindingClassification.KnownHistoricalJobError
                ? SubjectRecoveryRecipeId : null;
            findings.Add(CreateFinding("ActionableFailureGroup", classification,
                classification == CollectionFindingClassification.ProgramBug ? "high" : "medium",
                group.FirstFailedAt, now,
                $"{group.Definition.Value} has {group.Count} actionable {Sanitize(group.ErrorCode)} failures.",
                [
                    $"definition={Sanitize(group.Definition.Value)}",
                    $"status={group.Status}",
                    $"errorCode={Sanitize(group.ErrorCode)}",
                    $"count={group.Count}",
                    $"firstFailedAt={group.FirstFailedAt:O}",
                    $"lastFailedAt={group.LastFailedAt:O}",
                    $"sampleResources={string.Join(',', group.SampleResources.Take(5).Select(x => Sanitize($"{x.Type}/{x.Provider}/{x.Id}")))}",
                ],
                classification switch
                {
                    CollectionFindingClassification.ProgramBug => "Reproduce the failure and prepare a code-fix change record.",
                    CollectionFindingClassification.KnownHistoricalJobError => "Preview the registered recovery recipe and apply only safe candidates.",
                    _ => "Investigate the cause, impact, options, and safest next diagnostic step.",
                }, recipe,
                $"failure:{group.Definition.Value}:{group.Status}:{group.ErrorCode}:{classification}"));
        }

        if (snapshot.Pipeline.IsPaused && snapshot.Pipeline.UpdatedAt is { } pausedAt
            && now - pausedAt >= TimeSpan.FromMinutes(Math.Max(1, _options.UnexpectedPauseMinutes)))
        {
            findings.Add(CreateFinding("UnexpectedPipelinePause", CollectionFindingClassification.OperationalCondition,
                "high", pausedAt, now, "The collection pipeline has remained paused beyond the allowed duration.",
                [$"pausedAt={pausedAt:O}", $"reason={Sanitize(snapshot.Pipeline.Reason)}"],
                "Confirm whether the pause is intentional and resume only after the blocking condition is understood.",
                null, "pipeline:unexpected-pause"));
        }

        foreach (var group in snapshot.ActiveTasks
                     .Where(x => IsStalled(x, now))
                     .GroupBy(x => new { x.Definition.Value, x.Status, x.Lane, x.Priority }))
        {
            var oldest = group.Min(x => x.StartedAt ?? x.AvailableAt);
            var kind = group.Key.Status == CollectionTaskStatus.RetryWaiting
                ? "RetryWaitingBacklog" : "StalledActiveTask";
            findings.Add(CreateFinding(kind, CollectionFindingClassification.OperationalCondition,
                group.Key.Lane == CollectionLane.Realtime ? "high" : "medium", oldest, now,
                $"{group.Count()} {group.Key.Value} tasks are stalled in {group.Key.Status}.",
                [
                    $"definition={Sanitize(group.Key.Value)}",
                    $"status={group.Key.Status}",
                    $"lane={group.Key.Lane}",
                    $"priority={group.Key.Priority}",
                    $"oldest={oldest:O}",
                    $"sampleTaskIds={string.Join(',', group.Take(5).Select(x => x.TaskId.ToString("D")))}",
                ], "Inspect worker capacity, leases, availability, and the most recent attempts.", null,
                $"stalled:{group.Key.Value}:{group.Key.Status}:{group.Key.Lane}:{group.Key.Priority}"));
        }

        var domainFreshness = await GetDomainFreshnessAsync(snapshot.RaceFreshness ?? [], cancellationToken)
            .ConfigureAwait(false);
        findings.AddRange(EvaluateFreshness(snapshot.RaceFreshness ?? [], domainFreshness, now));

        foreach (var violation in FindDispatchOrderViolations(snapshot, now).Take(20))
            findings.Add(violation);

        var ordered = findings.OrderByDescending(x => SeverityRank(x.Severity)).ThenBy(x => x.Fingerprint)
            .Take(Math.Clamp(_options.MaxFindings, 1, 1_000)).ToArray();
        var hasUnmappedActionable = ordered.Any(x =>
            (x.Classification is CollectionFindingClassification.ProgramBug
                or CollectionFindingClassification.UnknownHistoricalJobError
                or CollectionFindingClassification.OperationalCondition)
            && string.IsNullOrWhiteSpace(x.OwnerTask));
        var outcome = hasUnmappedActionable
            ? CollectionMonitoringOutcome.MonitorFailed
            : ordered.Any(x => x.Kind == "UnexpectedPipelinePause"
                                       || x.Severity is "high" or "critical")
            ? CollectionMonitoringOutcome.ActionRequired
            : ordered.Length > 0
                ? CollectionMonitoringOutcome.FindingRecorded
                : CollectionMonitoringOutcome.Healthy;
        var flowDiagnostics = (snapshot.DefinitionFlows ?? []).Select(x => new CollectionDefinitionFlowDiagnostic(
            x.Definition.Value, x.Lane.ToString(), x.CompatibilityKey, x.Arrived, x.Dispatched, x.Completed,
            x.Active, x.OldestActiveAt is null ? 0 : Math.Max(0, (now - x.OldestActiveAt.Value).TotalMinutes),
            ClassifyFlow(x))).ToArray();
        return new(now, DateTimeOffset.UtcNow, true, _options.ChangeRecordEnabled, false, null,
            snapshot.Truncated, ordered, outcome, flowDiagnostics);
    }

    private static string ClassifyFlow(CollectionDefinitionFlowSnapshot flow)
    {
        if (flow.Active == 0) return "Healthy";
        if (flow.Dispatched == 0 && flow.Arrived > 0) return "StarvationOrCapabilityGap";
        if (flow.Arrived > flow.Completed && flow.Completed > 0) return "CapacityBelowArrivalRate";
        if (flow.OldestActiveAt is not null && flow.Arrived == 0) return "IntentionalWaitOrLegacyBacklog";
        return "NeedsEvidence";
    }

    internal IReadOnlyList<CollectionOperationalFinding> EvaluateFreshness(
        IReadOnlyList<CollectionRaceFreshnessSnapshot> snapshots, DateTimeOffset now)
        => EvaluateFreshness(snapshots, [], now);

    internal IReadOnlyList<CollectionOperationalFinding> EvaluateFreshness(
        IReadOnlyList<CollectionRaceFreshnessSnapshot> snapshots,
        IReadOnlyList<DomainRaceFreshness> domainRaces,
        DateTimeOffset now)
    {
        var findings = new List<CollectionOperationalFinding>();
        var latest = snapshots
            .Where(x => TryGetRaceDate(x.Resource.Id, out _))
            .GroupBy(x => x.Resource.Id, StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(y => y.UpdatedAt).First())
            .ToArray();

        var today = DateOnly.FromDateTime(now.Date);
        var cardDates = GetCardCheckpointDates(today, now.TimeOfDay).ToArray();
        foreach (var date in cardDates)
        {
            var races = latest.Where(x => TryGetRaceDate(x.Resource.Id, out var raceDate) && raceDate == date)
                .OrderBy(x => x.Resource.Id, StringComparer.Ordinal).ToArray();
            if (races.Length == 0)
            {
                findings.Add(CreateFinding("WeekendDiscoveryCoverageUnknown",
                    CollectionFindingClassification.OperationalCondition, "high", now, now,
                    $"Race discovery coverage for {date:yyyy-MM-dd} is unknown.",
                    [$"raceDate={date:yyyy-MM-dd}", "discovered=0", "coverage=unknown"],
                    "Inspect the calendar and race discovery path; zero discovered races is not treated as healthy.",
                    null, $"freshness:card-discovery:{date:yyyyMMdd}"));
                continue;
            }

            var domainCardCount = domainRaces.Count(x => x.RaceDate == date && x.HasCard);
            var artifactCardCount = races.Count(x => x.CardStatus == RaceArtifactStatus.Current);
            var missingCount = Math.Max(0, races.Length - Math.Max(artifactCardCount, domainCardCount));
            var missing = races.Where(x => x.CardStatus != RaceArtifactStatus.Current).Take(missingCount).ToArray();
            if (missing.Length == 0) continue;
            var severity = now.DayOfWeek == DayOfWeek.Friday
                           && now.Hour >= Math.Clamp(_options.FridayCriticalHour, 18, 23)
                ? "critical" : "high";
            findings.Add(CreateFinding("WeekendCardCoverageMissing",
                CollectionFindingClassification.OperationalCondition, severity,
                missing.Min(x => x.UpdatedAt), now,
                $"{missing.Length} of {races.Length} discovered races for {date:yyyy-MM-dd} do not have a current card.",
                [$"raceDate={date:yyyy-MM-dd}", $"discovered={races.Length}",
                    $"cardCurrent={races.Length - missing.Length}", $"missing={missing.Length}",
                    $"missingRaceIds={string.Join(',', missing.Take(20).Select(x => x.Resource.Id))}"],
                "Inspect race-detail card collection; do not wait for the result phase before persisting entries.",
                null, $"freshness:card:{date:yyyyMMdd}"));
        }

        var dueResults = latest.Where(x => x.OfficialStartAt is { } start
                                            && now >= start.AddMinutes(Math.Max(1, _options.ResultGraceMinutes)))
            .ToArray();
        foreach (var dateGroup in dueResults.GroupBy(x => GetRaceDate(x.Resource.Id)))
        {
            var domainResultCount = domainRaces.Count(x => x.RaceDate == dateGroup.Key && x.HasResult);
            var artifactResultCount = dateGroup.Count(x => x.ResultStatus == RaceArtifactStatus.Current
                                                            || x.ResultStatus == RaceArtifactStatus.Unavailable);
            var missingCount = Math.Max(0, dateGroup.Count() - Math.Max(artifactResultCount, domainResultCount));
            var missing = dateGroup.Where(x => x.ResultStatus != RaceArtifactStatus.Current
                                               && x.ResultStatus != RaceArtifactStatus.Unavailable)
                .Take(missingCount).ToArray();
            if (missing.Length == 0) continue;
            var checkpoint = new TimeSpan(Math.Clamp(_options.RaceDayResultCheckpointHour, 0, 23),
                Math.Clamp(_options.RaceDayResultCheckpointMinute, 0, 59), 0);
            var pastDayCheckpoint = dateGroup.Key == today && now.TimeOfDay >= checkpoint;
            findings.Add(CreateFinding(pastDayCheckpoint
                    ? "RaceDayResultCoverageMissing" : "RaceResultFreshnessMiss",
                CollectionFindingClassification.OperationalCondition,
                pastDayCheckpoint || missing.Any(x => now - x.OfficialStartAt!.Value > TimeSpan.FromHours(1))
                    ? "high" : "medium",
                missing.Min(x => x.OfficialStartAt!.Value.AddMinutes(Math.Max(1, _options.ResultGraceMinutes))), now,
                $"{missing.Length} of {dateGroup.Count()} due race results for {dateGroup.Key:yyyy-MM-dd} are not current.",
                [$"raceDate={dateGroup.Key:yyyy-MM-dd}", $"due={dateGroup.Count()}",
                    $"resultCurrent={dateGroup.Count() - missing.Length}", $"missing={missing.Length}",
                    $"missingRaceIds={string.Join(',', missing.Take(20).Select(x => x.Resource.Id))}"],
                "Inspect result publication and schedule an idempotent retry only through an approved recipe.",
                null, $"freshness:result:{dateGroup.Key:yyyyMMdd}:{(pastDayCheckpoint ? "day" : "race")}"));
        }
        return findings;
    }

    private async Task<IReadOnlyList<DomainRaceFreshness>> GetDomainFreshnessAsync(
        IReadOnlyList<CollectionRaceFreshnessSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (domainProvider is null) return [];
        var dates = snapshots.Select(x => GetRaceDate(x.Resource.Id)).Where(x => x != default).Distinct().ToArray();
        if (dates.Length == 0) return [];
        var first = dates.Min();
        var last = dates.Max();
        using var db = domainProvider.CreateContext();
        return await db.Set<RaceSummaryReadModel>().AsNoTracking()
            .Where(x => x.RaceDate >= first && x.RaceDate <= last)
            .Select(x => new DomainRaceFreshness(
                x.RaceDate!.Value,
                x.EntryCount.HasValue && x.EntryCount.Value > 0,
                x.ResultDeclaredAt.HasValue))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OwnerIdentityMigrationPreview> PreviewOwnerIdentityMigrationAsync(
        CancellationToken cancellationToken = default)
    {
        if (domainProvider is null)
            return new(0, 0, 0, 0, []);
        using var db = domainProvider.CreateContext();
        var horseNames = await db.Horses.AsNoTracking().Where(x => x.OwnerName != null)
            .Select(x => x.OwnerName!).ToListAsync(cancellationToken).ConfigureAwait(false);
        var contextNames = await db.RacePredictionContexts.AsNoTracking().ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var names = horseNames.Concat(contextNames.SelectMany(x => x.Entries)
                .Where(x => !string.IsNullOrWhiteSpace(x.OwnerName)).Select(x => x.OwnerName!))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var mapped = (await db.OwnerAliasMappings.AsNoTracking().Select(x => x.NormalizedAlias)
                .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var samples = names.Take(20).Select(name => new OwnerIdentityMigrationSample(name,
            OwnerIdentityContract.CreateId(name), OwnerIdentityContract.CreateLegacyId(name),
            mapped.Contains(OwnerIdentityContract.NormalizeName(name)))).ToArray();
        return new(names.Length,
            names.Select(OwnerIdentityContract.CreateId).Distinct(StringComparer.Ordinal).Count(),
            names.Select(OwnerIdentityContract.CreateLegacyId).Distinct(StringComparer.Ordinal).Count(),
            names.Count(x => mapped.Contains(OwnerIdentityContract.NormalizeName(x))), samples);
    }

    private IEnumerable<DateOnly> GetCardCheckpointDates(DateOnly today, TimeSpan localTime)
    {
        if (nowIsFriday(today, localTime))
        {
            yield return today.AddDays(1);
            yield return today.AddDays(2);
        }
        else if (today.DayOfWeek == DayOfWeek.Saturday)
        {
            yield return today;
            yield return today.AddDays(1);
        }
        else if (today.DayOfWeek == DayOfWeek.Sunday)
        {
            yield return today;
        }

        bool nowIsFriday(DateOnly date, TimeSpan time) => date.DayOfWeek == DayOfWeek.Friday
            && time >= TimeSpan.FromHours(Math.Clamp(_options.FridayCardCheckpointHour, 0, 23));
    }

    private static DateOnly GetRaceDate(string resourceId)
        => TryGetRaceDate(resourceId, out var date) ? date : default;

    private static bool TryGetRaceDate(string resourceId, out DateOnly date)
    {
        date = default;
        return resourceId.Length >= 8
               && DateOnly.TryParseExact(resourceId[..8], "yyyyMMdd", out date);
    }

    public async Task<CollectionKnownRecoveryPreview> PreviewKnownRecoveryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var report = await InspectAsync(now, cancellationToken).ConfigureAwait(false);
        var count = report.Suppressed ? 0
            : await SubjectIdentificationAutoRecovery.GetEligibleCandidateCountAsync(store, cancellationToken)
                .ConfigureAwait(false);
        var blocked = report.Suppressed ? report.SuppressionReason
            : !_options.RecoveryEnabled ? "Automatic recovery is disabled."
            : count == 0 ? "No matching known historical errors were found."
            : null;
        return new(SubjectRecoveryRecipeId, count, Math.Clamp(_options.CanaryLimit, 1, 100),
            _options.RecoveryEnabled, _options.MaintenanceMode, blocked is null, blocked);
    }

    public async Task<CollectionKnownRecoveryExecution> ApplyKnownRecoveryAsync(
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewKnownRecoveryAsync(now, cancellationToken).ConfigureAwait(false);
        if (!preview.SafeToApply)
            throw new InvalidOperationException(preview.BlockingReason ?? "Known recovery is not safe to apply.");
        if (!await RecoveryGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Another known recovery execution is already running.");
        try
        {
            var backup = await store.CreateMonitoringBackupAsync(now, cancellationToken).ConfigureAwait(false);
            var result = await SubjectIdentificationAutoRecovery.RunOnceAsync(store, logger, cancellationToken,
                Math.Clamp(_options.CanaryLimit, 1, 100)).ConfigureAwait(false);
            if (result.Failed > 0)
                throw new InvalidOperationException($"Known recovery stopped after {result.Failed} candidate failures.");
            return new(SubjectRecoveryRecipeId, backup.BackupId, backup.FileName,
                result.Examined, result.Recovered, result.Reused, result.Suppressed, result.Skipped, result.Failed);
        }
        finally
        {
            RecoveryGate.Release();
        }
    }

    private string? GetMaintenanceReason(CollectionPipelineState pipeline)
    {
        if (_options.MaintenanceMode) return "Collection monitoring maintenance mode is enabled.";
        if (!pipeline.IsPaused || string.IsNullOrWhiteSpace(pipeline.Reason)) return null;
        var reason = pipeline.Reason;
        return reason.Contains("maintenance", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("deploy", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("migration", StringComparison.OrdinalIgnoreCase)
            ? $"Collection maintenance is active: {Sanitize(reason)}" : null;
    }

    private CollectionFindingClassification ClassifyFailure(CollectionFailureGroup group)
    {
        if (string.Equals(group.ErrorCode, "SubjectNotIdentified", StringComparison.Ordinal)
            && group.SampleResources.All(x => x.Type is ResourceType.Horse or ResourceType.Jockey
                or ResourceType.Trainer))
            return CollectionFindingClassification.KnownHistoricalJobError;
        if (string.Equals(group.ErrorCode, "StructuralPageFailure", StringComparison.Ordinal)
            || string.Equals(group.ErrorCode, "ValidationFailure", StringComparison.Ordinal)
            || string.Equals(group.ErrorCode, "ParseFailure", StringComparison.Ordinal)
            || string.Equals(group.ErrorCode, "InvalidOperationException", StringComparison.Ordinal))
            return CollectionFindingClassification.ProgramBug;
        return CollectionFindingClassification.UnknownHistoricalJobError;
    }

    private bool IsStalled(CollectionMonitoringTaskSnapshot task, DateTimeOffset now) => task.Status switch
    {
        CollectionTaskStatus.Running => now - (task.StartedAt ?? task.UpdatedAt)
            >= TimeSpan.FromMinutes(Math.Max(1, _options.RunningStallMinutes)),
        CollectionTaskStatus.RetryWaiting => now - task.UpdatedAt
            >= TimeSpan.FromHours(Math.Max(1, _options.RetryWaitingHours)),
        CollectionTaskStatus.Pending or CollectionTaskStatus.Ready => task.AvailableAt <= now
            && now - task.AvailableAt >= TimeSpan.FromMinutes(Math.Max(1, _options.DueStallMinutes)),
        _ => false,
    };

    internal IEnumerable<CollectionOperationalFinding> FindDispatchOrderViolations(
        CollectionMonitoringSnapshot snapshot,
        DateTimeOffset now)
    {
        // An envelope can legitimately contain lower-priority compatible work. Treat a single
        // envelope as one dispatch decision and require repeated bypass of already-stalled work.
        var dispatches = snapshot.RecentDispatches
            .GroupBy(x => x.EnvelopeId == Guid.Empty ? x.TaskId : x.EnvelopeId)
            .Select(group => group.OrderByDescending(x => EffectivePriority(x.Priority, x.CreatedAt,
                    x.DispatchedAt))
                .ThenBy(x => x.AvailableAt).ThenBy(x => x.CreatedAt).ThenBy(x => x.TaskId).First())
            .ToArray();
        foreach (var higher in snapshot.ActiveTasks.Where(x => x.IsDispatchCandidate && IsStalled(x, now)))
        {
            if (string.IsNullOrWhiteSpace(higher.CompatibilityKey)) continue;
            var bypasses = dispatches.Where(dispatch => dispatch.Lane == higher.Lane
                    && string.Equals(dispatch.CompatibilityKey, higher.CompatibilityKey, StringComparison.Ordinal)
                    && higher.AvailableAt <= dispatch.DispatchedAt
                    && higher.CreatedAt <= dispatch.DispatchedAt
                    && EffectivePriority(higher.Priority, higher.CreatedAt, dispatch.DispatchedAt)
                    > EffectivePriority(dispatch.Priority, dispatch.CreatedAt, dispatch.DispatchedAt))
                .OrderBy(x => x.DispatchedAt)
                .ToArray();
            if (bypasses.Length < 3) continue;
            var first = bypasses[0];
            yield return CreateFinding("DispatchOrderViolation", CollectionFindingClassification.ProgramBug,
                "high", first.DispatchedAt, now,
                "Stalled higher-priority work was bypassed by at least three dispatch decisions in the same lane.",
                [
                    $"lane={higher.Lane}",
                    $"compatibilityKey={higher.CompatibilityKey}",
                    $"waitingDispatchCandidate={higher.IsDispatchCandidate}",
                    $"waitingTaskId={higher.TaskId:D}",
                    $"waitingPriority={higher.Priority}",
                    $"bypassCount={bypasses.Length}",
                    $"firstBypassAt={first.DispatchedAt:O}",
                    $"sampleEnvelopeIds={string.Join(',', bypasses.Take(5).Select(x => x.EnvelopeId.ToString("D")))}",
                ], "Reproduce the dispatcher selection at the recorded cutoff and fix the ordering contract.",
                null, $"dispatch-order:{higher.Lane}:{higher.Definition.Value}");
        }
    }

    private CollectionOperationalFinding CreateFinding(string kind,
        CollectionFindingClassification classification, string severity,
        DateTimeOffset first, DateTimeOffset last, string summary, IReadOnlyList<string> evidence,
        string suggestedScope, string? recipe, string key)
    {
        var raw = $"{_options.ClassifierVersion}|{kind}|{classification}|{key}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        var routing = RouteRootCause(kind, key);
        return new(fingerprint, kind, classification, severity, first, last, Sanitize(summary),
            evidence.Select(Sanitize).Where(x => !string.IsNullOrWhiteSpace(x)).Take(20).ToArray(),
            Sanitize(suggestedScope), recipe, _options.ClassifierVersion,
            routing.Hypothesis, routing.OwnerTask, routing.NextSafeOperation);
    }

    private static (string Hypothesis, string OwnerTask, string NextSafeOperation) RouteRootCause(
        string kind, string key) => (kind, key) switch
        {
            ("ActionableFailureGroup", var value) when value.Contains("owner", StringComparison.OrdinalIgnoreCase) =>
                ("Owner identity producer and lookup contracts may disagree.", "T1",
                    "Run the owner identity compatibility preview; do not rewrite stored IDs."),
            ("DispatchOrderViolation", _) =>
                ("Compatible higher-priority work may have been repeatedly bypassed.", "T2",
                    "Inspect the recorded compatibility definition and envelope sequence."),
            ("StalledActiveTask" or "RetryWaitingBacklog", _) =>
                ("Arrival, dispatch, or completion capacity may be imbalanced.", "T3",
                    "Compare definition flow rates and oldest age at the same cutoff."),
            ("ActionableFailureGroup", var value) when value.Contains("TargetClosed", StringComparison.OrdinalIgnoreCase) =>
                ("The observation may predate the deployed closed-session recovery revision.", "T6",
                    "Confirm deployed revision and re-observe before creating another fix."),
            ("WeekendCardCoverageMissing" or "WeekendDiscoveryCoverageUnknown" or "RaceResultFreshnessMiss"
                or "RaceDayResultCoverageMissing", _) =>
                ("Required race data is not confirmed in the domain by its checkpoint.", "T6",
                    "Verify deployed revision and inspect the read-only freshness evidence."),
            _ => ("The finding requires consolidated operational triage.", "T3",
                "Inspect the definition flow diagnostic and representative task evidence."),
        };

    private static int EffectivePriority(int priority, DateTimeOffset createdAt, DateTimeOffset now)
        => priority + Math.Min(30, Math.Max(0, (int)(now - createdAt).TotalHours / 6));

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        _ => 1,
    };

    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = new string(value.Where(ch => !char.IsControl(ch) || ch == ' ').ToArray())
            .Replace("```", "'''", StringComparison.Ordinal)
            .Replace("<!--", "&lt;!--", StringComparison.Ordinal)
            .Replace("-->", "--&gt;", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
