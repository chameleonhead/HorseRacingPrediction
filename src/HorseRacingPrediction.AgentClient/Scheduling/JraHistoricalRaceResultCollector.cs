using System.Globalization;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class JraHistoricalRaceResultCollector : IHistoricalRaceResultCollector
{
    private readonly IJraRaceResultLookup _raceResultLookup;
    private readonly DataCollectionWriteTools _writeTools;
    private readonly ProcessingStateStore _stateStore;

    public JraHistoricalRaceResultCollector(
        IJraRaceResultLookup raceResultLookup,
        DataCollectionWriteTools writeTools,
        ProcessingStateStore stateStore)
    {
        _raceResultLookup = raceResultLookup;
        _writeTools = writeTools;
        _stateStore = stateStore;
    }

    public async Task<HistoricalDataRequestExecutionResult> CollectAsync(
        HistoricalRaceResultCollectionRequestPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(payload.ProviderType, "JRA", StringComparison.OrdinalIgnoreCase))
        {
            return HistoricalDataRequestExecutionResult.PermanentFailure(
                $"Race result collection is not supported for provider '{payload.ProviderType}'.");
        }

        try
        {
            var racecourse = JraRacecourseResolver.ResolveDisplayName(payload.Racecourse) ?? payload.Racecourse;
            var raceId = DeterministicIdGenerator.BuildRaceId(payload.RaceDate, racecourse, payload.RaceNumber);

            await _stateStore.UpsertRaceResultCollectionStatusAsync(
                payload.RaceDate,
                racecourse,
                payload.RaceNumber,
                raceId,
                raceName: null,
                sourceUrl: null,
                RaceDataCollectionState.Running,
                RaceResultAcquisitionOrigin.HistoricalDependency,
                payload.RequestedByRaceId,
                errorCode: null,
                errorReason: null,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);

            JraExtractionEnvelope<JraRaceResultSummary> extraction = await _raceResultLookup
                .GetRaceResultAsync(payload.RaceDate, racecourse, payload.RaceNumber, cancellationToken)
                .ConfigureAwait(false);

            if (!extraction.Success || extraction.Data is null)
            {
                var error = RaceDataCollectionErrorClassifier.Classify(extraction.Error);
                await _stateStore.UpsertRaceResultCollectionStatusAsync(
                    payload.RaceDate,
                    racecourse,
                    payload.RaceNumber,
                    raceId,
                    raceName: null,
                    sourceUrl: extraction.SourceUrl,
                    RaceDataCollectionState.Failed,
                    RaceResultAcquisitionOrigin.HistoricalDependency,
                    payload.RequestedByRaceId,
                    error.Code,
                    error.Reason,
                    DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);

                return HistoricalDataRequestExecutionResult.Retry(
                    $"Failed to fetch historical race result. Date={payload.RaceDate:yyyy-MM-dd} Racecourse={racecourse} RaceNumber={payload.RaceNumber} Error={extraction.Error ?? "unknown"}");
            }

            var result = extraction.Data;
            raceId = await _writeTools.UpsertRace(
                payload.RaceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                racecourse,
                payload.RaceNumber,
                string.IsNullOrWhiteSpace(result.RaceName) ? $"R{payload.RaceNumber}" : result.RaceName!,
                result.Entries.Count > 0 ? result.Entries.Count : null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var winner = result.Entries.FirstOrDefault(x => x.FinishPosition == 1 && !string.IsNullOrWhiteSpace(x.HorseName));
            if (winner is null)
            {
                var error = RaceDataCollectionErrorClassifier.Classify("Historical race result did not contain a winner.");
                await _stateStore.UpsertRaceResultCollectionStatusAsync(
                    payload.RaceDate,
                    racecourse,
                    payload.RaceNumber,
                    raceId,
                    result.RaceName,
                    extraction.SourceUrl,
                    RaceDataCollectionState.Failed,
                    RaceResultAcquisitionOrigin.HistoricalDependency,
                    payload.RequestedByRaceId,
                    error.Code,
                    error.Reason,
                    DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);

                return HistoricalDataRequestExecutionResult.Retry(
                    $"Historical race result did not contain a winner. RaceId={raceId}");
            }

            foreach (var entry in result.Entries.Where(x => !string.IsNullOrWhiteSpace(x.HorseName)))
            {
                await _writeTools.UpsertRaceEntry(
                    raceId,
                    entry.HorseNumber,
                    entry.HorseName!,
                    entry.JockeyName,
                    trainerName: null,
                    gateNumber: entry.GateNumber,
                    assignedWeight: entry.AssignedWeight,
                    sexCode: null,
                    age: null,
                    declaredWeight: entry.DeclaredWeight,
                    declaredWeightDiff: entry.DeclaredWeightDiff,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await _writeTools.DeclareRaceResult(raceId, winner.HorseName!, cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var entry in result.Entries)
            {
                await _writeTools.DeclareRaceEntryResult(
                    raceId,
                    entry.HorseNumber,
                    entry.FinishPosition,
                    entry.FinishTime,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await _stateStore.UpsertRaceResultCollectionStatusAsync(
                payload.RaceDate,
                racecourse,
                payload.RaceNumber,
                raceId,
                result.RaceName,
                extraction.SourceUrl,
                RaceDataCollectionState.Succeeded,
                RaceResultAcquisitionOrigin.HistoricalDependency,
                payload.RequestedByRaceId,
                errorCode: null,
                errorReason: null,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Success(
                $"Historical race result synchronized. RaceId={raceId}");
        }
        catch (Exception ex)
        {
            var error = RaceDataCollectionErrorClassifier.Classify(ex.Message, ex);
            await _stateStore.UpsertRaceResultCollectionStatusAsync(
                payload.RaceDate,
                JraRacecourseResolver.ResolveDisplayName(payload.Racecourse) ?? payload.Racecourse,
                payload.RaceNumber,
                raceId: null,
                raceName: null,
                sourceUrl: null,
                RaceDataCollectionState.Failed,
                RaceResultAcquisitionOrigin.HistoricalDependency,
                payload.RequestedByRaceId,
                error.Code,
                error.Reason,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Retry(
                $"Historical race result synchronization failed. Date={payload.RaceDate:yyyy-MM-dd} Racecourse={payload.Racecourse} RaceNumber={payload.RaceNumber}. {ex.Message}");
        }
    }
}