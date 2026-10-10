using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionScheduleService
{
    private readonly CollectionPlatformStore store;
    private readonly ILogger<CollectionScheduleService> logger;
    private readonly CollectionRuntimeStatusRecorder? _runtimeStatus;
    private readonly ICollectionSchedulePolicy _policy;
    private CollectionScheduleCursor? _sweepCursor;
    private DateTimeOffset? _sweepHorizon;
    private int _stableNoProgressPeriods;
    private int _lastCandidateCount;
    private DateTimeOffset? _lastOldestDueAt;
    private bool _stalledWarningEmitted;

    public CollectionScheduleService(CollectionPlatformStore store,
        IEnumerable<ICollectionSchedulePolicy> policies, ILogger<CollectionScheduleService> logger)
        : this(store, policies, logger, null)
    {
    }

    internal CollectionScheduleService(CollectionPlatformStore store,
        IEnumerable<ICollectionSchedulePolicy> policies, ILogger<CollectionScheduleService> logger,
        CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        this.store = store;
        this.logger = logger;
        _policy = policies.Single();
        _runtimeStatus = runtimeStatus;
    }

    internal void ResetSweepCursor()
    {
        _sweepCursor = null;
        _sweepHorizon = null;
        _stableNoProgressPeriods = 0;
        _lastCandidateCount = 0;
        _lastOldestDueAt = null;
        _stalledWarningEmitted = false;
    }

    internal async Task RunOnceAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? shouldContinue = null,
        Func<CollectionRuntimeReason?>? getStopReason = null)
    {
        var token = _runtimeStatus?.BeginCycle(CollectionRuntimeAction.RefreshPlanner);
        const int pageSize = 500;
        shouldContinue ??= static _ => Task.FromResult(true);
        var inspected = 0;
        var created = 0;
        var eligible = 0;
        var held = false;
        CollectionRuntimeReason? stoppedReason = null;
        try
        {
            _sweepHorizon ??= now;
            var candidates = await store.GetDueScheduleCandidatesAsync(_sweepHorizon.Value, _sweepCursor,
                pageSize, cancellationToken).ConfigureAwait(false);
            var pageCompleted = true;
            foreach (var candidate in candidates)
            {
                if (!await shouldContinue(cancellationToken).ConfigureAwait(false))
                {
                    pageCompleted = false;
                    stoppedReason = getStopReason?.Invoke() ?? CollectionRuntimeReason.BudgetExhausted;
                    break;
                }

                inspected++;
                var state = candidate.State;
                var schedule = _policy.Evaluate(state.Resource, state, now);
                var decision = CollectionScheduleSweepPolicy.Decide(candidate, schedule, now);
                if (!decision.ShouldCollect)
                {
                    _sweepCursor = decision.NextCursor;
                    continue;
                }
                eligible++;

                try
                {
                    var attempt = await store.TryScheduleDueDetailedAsync(candidate, schedule, now, _policy,
                        cancellationToken).ConfigureAwait(false);
                    _sweepCursor = attempt.Decision.NextCursor;
                    held |= attempt.WasDeferred || attempt.Decision.Reason == "repair-held";
                    if (attempt.HasProgress)
                    {
                        created++;
                        if (token is { } progressToken) _runtimeStatus!.RecordProgress(progressToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to schedule due resource {Resource}/{Definition}",
                        state.Resource, state.Definition);
                    pageCompleted = false;
                    if (token is { } failedToken)
                        _runtimeStatus!.FailCycle(failedToken, new(inspected, created));
                    return;
                }
            }

            if (pageCompleted)
            {
                var progress = CollectionScheduleSweepPolicy.CompletePage(
                    new(_sweepHorizon, _sweepCursor), candidates.Count, pageSize);
                _sweepCursor = progress.Cursor;
                _sweepHorizon = progress.Horizon;
            }

            TrackSweepHealth(candidates, now, eligible, held, created, inspected);

            var reason = stoppedReason ?? (held ? CollectionRuntimeReason.RepairHeld
                : created == 0 ? CollectionRuntimeReason.NoDueWork : null);
            if (token is { } completedToken)
                _runtimeStatus!.CompleteCycle(completedToken, reason, new(inspected, created));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (token is { } cancelledToken) _runtimeStatus!.CancelCycle(cancelledToken, new(inspected, created));
            throw;
        }
        catch
        {
            if (token is { } failedToken) _runtimeStatus!.FailCycle(failedToken, new(inspected, created));
            throw;
        }
    }

    internal static bool ShouldReportStalledSweep(DateTimeOffset now, DateTimeOffset oldestDueAt,
        int stableNoProgressPeriods, bool hasEligibleCandidate, bool hasRepairHeldCandidate) =>
        stableNoProgressPeriods >= 3 && hasEligibleCandidate && !hasRepairHeldCandidate
        && now - oldestDueAt >= TimeSpan.FromMinutes(3);

    private void TrackSweepHealth(IReadOnlyList<CollectionScheduleCandidate> candidates, DateTimeOffset now,
        int eligible, bool held, int created, int inspected)
    {
        if (created > 0 || candidates.Count == 0 || eligible == 0 || held)
        {
            _stableNoProgressPeriods = 0;
            _lastCandidateCount = 0;
            _lastOldestDueAt = null;
            _stalledWarningEmitted = false;
            return;
        }

        var oldestDueAt = candidates[0].State.NextCollectionAt ?? now;
        if (_lastCandidateCount == candidates.Count && _lastOldestDueAt == oldestDueAt)
            _stableNoProgressPeriods++;
        else
            _stableNoProgressPeriods = 1;
        _lastCandidateCount = candidates.Count;
        _lastOldestDueAt = oldestDueAt;

        if (!_stalledWarningEmitted && ShouldReportStalledSweep(now, oldestDueAt,
                _stableNoProgressPeriods, hasEligibleCandidate: true, hasRepairHeldCandidate: false))
        {
            _stalledWarningEmitted = true;
            logger.LogWarning("Collection due-schedule sweep has no progress for {StablePeriods} producer periods; "
                + "sweep age={SweepAgeSeconds}s inspected={InspectedCount} created={CreatedCount}.",
                _stableNoProgressPeriods, Math.Max(0, (long)(now - oldestDueAt).TotalSeconds), inspected, created);
        }
    }
}
