using System.Threading.Channels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum HorseIdentityMigrationExecutionRequestResult
{
    Accepted,
    NotFound,
    Conflict,
    Unavailable,
}

/// <summary>Per-host, explicitly awakened preparation for a single persisted migration intent.</summary>
internal sealed class HorseIdentityMigrationPreparationService : BackgroundService
{
    internal const int ApplyReadyExitCode = 73;
    private static readonly TimeSpan PauseDuration = TimeSpan.FromSeconds(900);
    private static readonly TimeSpan RunningDrainLimit = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FailureRecordTimeout = TimeSpan.FromSeconds(2);
    internal static TimeSpan RequiredPauseDuration => PauseDuration;

    private readonly HorseIdentityMigrationJournal _journal;
    private readonly CollectionPlatformStore _collection;
    private readonly HorseIdentityMigrationRuntimeOwnership? _runtimeOwnership;
    private readonly HorseIdentityMigrationMaintenanceCapability.ApiBootstrapAuthorization? _bootstrapAuthorization;
    private readonly TimeProvider _timeProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<HorseIdentityMigrationPreparationService> _logger;
    private readonly Channel<Guid> _requests = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    internal HorseIdentityMigrationPreparationService(
        HorseIdentityMigrationJournal journal,
        CollectionPlatformStore collection,
        HorseIdentityMigrationRuntimeOwnership? runtimeOwnership,
        HorseIdentityMigrationMaintenanceCapability.ApiBootstrapAuthorization? bootstrapAuthorization,
        TimeProvider timeProvider,
        IHostApplicationLifetime lifetime,
        ILogger<HorseIdentityMigrationPreparationService> logger)
    {
        _journal = journal;
        _collection = collection;
        _runtimeOwnership = runtimeOwnership;
        _bootstrapAuthorization = bootstrapAuthorization;
        _timeProvider = timeProvider;
        _lifetime = lifetime;
        _logger = logger;
    }

    internal bool IsEnabled => _runtimeOwnership is not null && _bootstrapAuthorization is not null;

    internal async Task<HorseIdentityMigrationExecutionRequestResult> RequestExecutionAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        if (!IsEnabled) return HorseIdentityMigrationExecutionRequestResult.Unavailable;
        if (operationId == Guid.Empty) return HorseIdentityMigrationExecutionRequestResult.NotFound;

        var operation = await _journal.GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation is null) return HorseIdentityMigrationExecutionRequestResult.NotFound;
        if (operation.Status != HorseIdentityMigrationOperationStatus.Staged)
            return HorseIdentityMigrationExecutionRequestResult.Conflict;

        var request = await _journal.GetExecutableRequestAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (request is null) return HorseIdentityMigrationExecutionRequestResult.Conflict;

        var intent = await _journal.GetExecutionIntentAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (intent is null)
        {
            var created = await _journal.CreateExecutionIntentAsync(operationId, request.ImmutableRequestHash,
                _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            if (!created.Succeeded) return HorseIdentityMigrationExecutionRequestResult.Conflict;
            intent = created.Intent;
        }
        else if (!string.Equals(intent.ImmutableRequestHash, request.ImmutableRequestHash, StringComparison.Ordinal))
        {
            return HorseIdentityMigrationExecutionRequestResult.Conflict;
        }

        // Dispatch is deliberately independent from the HTTP request token after the
        // journal commit. A retry can wake an intent if the first response was lost.
        if (intent!.Phase == HorseIdentityMigrationExecutionPhase.IntentRecorded)
            await _requests.Writer.WriteAsync(operationId, CancellationToken.None).ConfigureAwait(false);
        return HorseIdentityMigrationExecutionRequestResult.Accepted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled) return;
        await foreach (var operationId in _requests.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await PrepareAsync(operationId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await MarkFailedAsync(operationId).ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Horse identity migration preparation stopped safely for {OperationId}.",
                    operationId);
                await MarkFailedAsync(operationId).ConfigureAwait(false);
            }
        }
    }

    private async Task PrepareAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var intent = await _journal.GetExecutionIntentAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (intent?.Phase != HorseIdentityMigrationExecutionPhase.IntentRecorded) return;
        var operation = await _journal.GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation?.Status != HorseIdentityMigrationOperationStatus.Staged) return;
        if (!(await _journal.AdvanceExecutionIntentAsync(operationId, HorseIdentityMigrationExecutionPhase.Preparing,
                _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false)).Succeeded) return;

        var initialPause = await _collection.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false);
        if (!initialPause.IsPaused || initialPause.UpdatedAt is null)
            throw new InvalidOperationException("Collection must be explicitly paused before migration preparation.");

        await _journal.AdvanceExecutionIntentAsync(operationId, HorseIdentityMigrationExecutionPhase.Draining,
            _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var drainStarted = _timeProvider.GetUtcNow();
        var pauseStartedAt = initialPause.UpdatedAt.Value;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pause = await _collection.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false);
            if (!pause.IsPaused || pause.UpdatedAt != pauseStartedAt)
                throw new InvalidOperationException("Collection pause changed during migration preparation.");

            var running = await _collection.GetTasksAsync(CollectionTaskStatus.Running, 1, cancellationToken)
                .ConfigureAwait(false);
            if (IsReadyToStop(pause, pauseStartedAt, running.Count, _timeProvider.GetUtcNow())) break;
            if (running.Count != 0 && _timeProvider.GetUtcNow() >= drainStarted + RunningDrainLimit)
                throw new TimeoutException("Collection work did not drain within the bounded preparation window.");

            await Task.Delay(PollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        var finalPause = await _collection.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false);
        var finalRunning = await _collection.GetTasksAsync(CollectionTaskStatus.Running, 1, cancellationToken)
            .ConfigureAwait(false);
        if (!IsReadyToStop(finalPause, pauseStartedAt, finalRunning.Count, _timeProvider.GetUtcNow()))
            throw new InvalidOperationException("Collection state changed before the API stop handoff.");

        if (!(await _journal.AdvanceExecutionIntentAsync(operationId, HorseIdentityMigrationExecutionPhase.StoppingApi,
                _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false)).Succeeded)
            throw new InvalidOperationException("The migration intent could not enter the API stop handoff.");
        await HorseIdentityMigrationMaintenanceOwnership.InstallMaintenanceGatesAsync(
            _runtimeOwnership!, _bootstrapAuthorization!, operationId, intent.ImmutableRequestHash,
            _journal, cancellationToken).ConfigureAwait(false);

        // The supervisor accepts this private exit only after observing this process
        // fully exit and revalidating the durable intent plus exact installed gates.
        Environment.ExitCode = ApplyReadyExitCode;
        _lifetime.StopApplication();
    }

    internal static bool IsReadyToStop(CollectionPipelineState state, DateTimeOffset pauseStartedAt,
        int runningCount, DateTimeOffset now) => state.IsPaused && state.UpdatedAt == pauseStartedAt
        && runningCount == 0 && now >= pauseStartedAt + PauseDuration;

    private async Task MarkFailedAsync(Guid operationId)
    {
        var failureAt = _timeProvider.GetUtcNow();
        try
        {
            using var deadline = new CancellationTokenSource(FailureRecordTimeout);
            await _journal.RecordFailureAsync(operationId, HorseIdentityMigrationResultCode.PreparationFailed,
                failureAt, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not persist the categorized preparation failure for {OperationId}.",
                operationId);
        }

        try
        {
            using var deadline = new CancellationTokenSource(FailureRecordTimeout);
            await _journal.AdvanceExecutionIntentAsync(operationId, HorseIdentityMigrationExecutionPhase.Failed,
                failureAt, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not persist the failed preparation state for {OperationId}.",
                operationId);
        }
    }
}
