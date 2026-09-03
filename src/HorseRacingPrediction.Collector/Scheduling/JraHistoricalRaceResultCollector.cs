// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using System.Globalization;
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraHistoricalRaceResultCollector : IHistoricalRaceResultCollector
{
    private readonly IJraRaceResultLookup _raceResultLookup;
    private readonly DataCollectionWriteTools _writeTools;
    private readonly IProcessingStateStore _stateStore;

    public JraHistoricalRaceResultCollector(
        IJraRaceResultLookup raceResultLookup,
        DataCollectionWriteTools writeTools,
        IProcessingStateStore stateStore)
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
                sourceUrl: payload.SourceUrl,
                RaceDataCollectionState.Running,
                RaceResultAcquisitionOrigin.HistoricalDependency,
                payload.RequestedByRaceId,
                errorCode: null,
                errorReason: null,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);

            JraExtractionEnvelope<JraRaceResultSummary> extraction = !string.IsNullOrWhiteSpace(payload.SourceUrl)
                ? await _raceResultLookup.GetRaceResultByUrlAsync(payload.SourceUrl, cancellationToken).ConfigureAwait(false)
                : await _raceResultLookup.GetRaceResultAsync(
                    payload.RaceDate, racecourse, payload.RaceNumber, cancellationToken).ConfigureAwait(false);

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
            ValidateCourseInformation(result, extraction.SourceUrl);
            var validEntries = result.Entries
                .Where(x => !string.IsNullOrWhiteSpace(x.HorseName))
                .ToList();

            raceId = await _writeTools.UpsertRace(
                payload.RaceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                racecourse,
                payload.RaceNumber,
                string.IsNullOrWhiteSpace(result.RaceName) ? $"R{payload.RaceNumber}" : result.RaceName!,
                validEntries.Count > 0 ? validEntries.Count : null,
                gradeCode: result.GradeCode,
                surfaceCode: result.SurfaceCode,
                distanceMeters: result.DistanceMeters,
                directionCode: result.DirectionCode,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var winner = validEntries.FirstOrDefault(x => x.FinishPosition == 1);
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

            foreach (var entry in validEntries)
            {
                var (sexCode, age) = ParseSexAge(entry.SexAge);
                await _writeTools.UpsertRaceEntry(
                    raceId,
                    entry.HorseNumber,
                    entry.HorseName!,
                    entry.JockeyName!,
                    trainerName: null,
                    gateNumber: entry.GateNumber,
                    assignedWeight: entry.AssignedWeight,
                    sexCode: sexCode,
                    age: age,
                    declaredWeight: entry.DeclaredWeight,
                    declaredWeightDiff: entry.DeclaredWeightDiff,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await _writeTools.DeclareRaceResult(raceId, winner.HorseName!, cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var entry in validEntries)
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
                sourceUrl: payload.SourceUrl,
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

    private static (string? SexCode, int? Age) ParseSexAge(string? sexAge)
    {
        return JraSexAgeParser.Parse(sexAge);
    }

    private static void ValidateCourseInformation(JraRaceResultSummary result, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(result.SurfaceCode)
            || result.DistanceMeters is null or <= 0
            || string.IsNullOrWhiteSpace(result.GradeCode))
        {
            throw new InvalidOperationException(
                $"結果コース情報バリデーションエラー: raceName='{result.RaceName}', surface='{result.SurfaceCode}', distanceMeters='{result.DistanceMeters}', grade='{result.GradeCode}', sourceUrl='{sourceUrl}'");
        }
    }
}
#endif
