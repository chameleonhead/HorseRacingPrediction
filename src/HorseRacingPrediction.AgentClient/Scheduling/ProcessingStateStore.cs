using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class ProcessingStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _stateFilePath;
    private readonly ILogger<ProcessingStateStore> _logger;

    public ProcessingStateStore(IOptions<AgentProcessingOptions> options, ILogger<ProcessingStateStore> logger)
    {
        var dir = options.Value.StateDirectory;
        var stateDirectory = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(AppContext.BaseDirectory, "agent-processing-state")
            : dir;

        Directory.CreateDirectory(stateDirectory);
        _stateFilePath = Path.Combine(stateDirectory, "processing-state.json");
        _logger = logger;
    }

    public async Task EnqueuePredictionCandidatesAsync(
        IEnumerable<string> raceIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateInternalAsync(cancellationToken).ConfigureAwait(false);
            var pendingSet = state.PendingPredictions.Select(x => x.RaceId).ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var raceId in raceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                if (state.CompletedPredictions.Contains(raceId) || pendingSet.Contains(raceId))
                {
                    continue;
                }

                state.PendingPredictions.Add(new PendingPredictionState
                {
                    RaceId = raceId,
                    FirstQueuedAt = now,
                    RetryCount = 0,
                    LastError = null
                });
                pendingSet.Add(raceId);
                added++;
            }

            if (added > 0)
            {
                await SaveStateInternalAsync(state, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> TakeReadyPredictionCandidatesAsync(
        DateTimeOffset now,
        TimeSpan minAge,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateInternalAsync(cancellationToken).ConfigureAwait(false);

            var ready = state.PendingPredictions
                .Where(x => now - x.FirstQueuedAt >= minAge)
                .OrderBy(x => x.FirstQueuedAt)
                .Take(Math.Max(1, maxCount))
                .ToList();

            if (ready.Count == 0)
            {
                return [];
            }

            var ids = ready.Select(x => x.RaceId).ToHashSet(StringComparer.Ordinal);
            state.PendingPredictions.RemoveAll(x => ids.Contains(x.RaceId));
            await SaveStateInternalAsync(state, cancellationToken).ConfigureAwait(false);

            return ready.Select(x => x.RaceId).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkPredictionCompletedAsync(string raceId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateInternalAsync(cancellationToken).ConfigureAwait(false);
            if (state.CompletedPredictions.Add(raceId))
            {
                await SaveStateInternalAsync(state, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RequeuePredictionCandidateAsync(
        string raceId,
        DateTimeOffset now,
        string error,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateInternalAsync(cancellationToken).ConfigureAwait(false);
            var existing = state.PendingPredictions.FirstOrDefault(x => x.RaceId == raceId);
            if (existing is null)
            {
                state.PendingPredictions.Add(new PendingPredictionState
                {
                    RaceId = raceId,
                    FirstQueuedAt = now,
                    RetryCount = 1,
                    LastError = error
                });
            }
            else
            {
                existing.FirstQueuedAt = now;
                existing.RetryCount += 1;
                existing.LastError = error;
            }

            await SaveStateInternalAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsTextInsightRecordedAsync(string insightKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateInternalAsync(cancellationToken).ConfigureAwait(false);
            return state.RecordedTextInsightKeys.Contains(insightKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkTextInsightRecordedAsync(string insightKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadStateInternalAsync(cancellationToken).ConfigureAwait(false);
            if (state.RecordedTextInsightKeys.Add(insightKey))
            {
                await SaveStateInternalAsync(state, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProcessingState> LoadStateInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_stateFilePath))
        {
            return new ProcessingState();
        }

        try
        {
            await using var stream = File.OpenRead(_stateFilePath);
            var state = await JsonSerializer.DeserializeAsync<ProcessingState>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return state ?? new ProcessingState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "状態ファイル読み込みに失敗したため空状態で再開します。Path={Path}", _stateFilePath);
            return new ProcessingState();
        }
    }

    private async Task SaveStateInternalAsync(ProcessingState state, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_stateFilePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private sealed class ProcessingState
    {
        public List<PendingPredictionState> PendingPredictions { get; set; } = [];
        public HashSet<string> CompletedPredictions { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> RecordedTextInsightKeys { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PendingPredictionState
    {
        public string RaceId { get; set; } = string.Empty;
        public DateTimeOffset FirstQueuedAt { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
    }
}
