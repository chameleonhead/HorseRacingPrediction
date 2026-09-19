using System.Security.Cryptography;
using System.Text;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
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
    string ClassifierVersion);

public sealed record CollectionMonitoringReport(
    DateTimeOffset Cutoff,
    DateTimeOffset CompletedAt,
    bool Enabled,
    bool ChangeRecordEnabled,
    bool Suppressed,
    string? SuppressionReason,
    bool Truncated,
    IReadOnlyList<CollectionOperationalFinding> Findings,
    CollectionMonitoringOutcome Outcome = CollectionMonitoringOutcome.Healthy);

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

public sealed class CollectionMonitoringService(
    CollectionPlatformStore store,
    IOptions<CollectionMonitoringOptions> options)
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

        findings.AddRange(EvaluateFreshness(snapshot.RaceFreshness ?? [], now));

        foreach (var violation in FindDispatchOrderViolations(snapshot, now).Take(20))
            findings.Add(violation);

        var ordered = findings.OrderByDescending(x => SeverityRank(x.Severity)).ThenBy(x => x.Fingerprint)
            .Take(Math.Clamp(_options.MaxFindings, 1, 1_000)).ToArray();
        var outcome = ordered.Any(x => x.Kind == "UnexpectedPipelinePause"
                                       || x.Severity is "high" or "critical")
            ? CollectionMonitoringOutcome.ActionRequired
            : ordered.Length > 0
                ? CollectionMonitoringOutcome.FindingRecorded
                : CollectionMonitoringOutcome.Healthy;
        return new(now, DateTimeOffset.UtcNow, true, _options.ChangeRecordEnabled, false, null,
            snapshot.Truncated, ordered, outcome);
    }

    internal IReadOnlyList<CollectionOperationalFinding> EvaluateFreshness(
        IReadOnlyList<CollectionRaceFreshnessSnapshot> snapshots, DateTimeOffset now)
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

            var missing = races.Where(x => x.CardStatus != RaceArtifactStatus.Current).ToArray();
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
            var missing = dateGroup.Where(x => x.ResultStatus != RaceArtifactStatus.Current
                                               && x.ResultStatus != RaceArtifactStatus.Unavailable).ToArray();
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

    private IEnumerable<CollectionOperationalFinding> FindDispatchOrderViolations(
        CollectionMonitoringSnapshot snapshot,
        DateTimeOffset now)
    {
        // An envelope can legitimately contain lower-priority compatible work. Treat a single
        // envelope as one dispatch decision and require repeated bypass of already-stalled work.
        var dispatches = snapshot.RecentDispatches
            .GroupBy(x => x.EnvelopeId == Guid.Empty ? x.TaskId : x.EnvelopeId)
            .Select(group => group.OrderByDescending(x => x.Priority).First())
            .ToArray();
        foreach (var higher in snapshot.ActiveTasks.Where(x => IsStalled(x, now)))
        {
            var bypasses = dispatches.Where(dispatch => dispatch.Lane == higher.Lane
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
        return new(fingerprint, kind, classification, severity, first, last, Sanitize(summary),
            evidence.Select(Sanitize).Where(x => !string.IsNullOrWhiteSpace(x)).Take(20).ToArray(),
            Sanitize(suggestedScope), recipe, _options.ClassifierVersion);
    }

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
