using System.Globalization;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.Scraping.Scrapers.Jra;
using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraHistoricalDataRequestHandler : IHistoricalDataRequestHandler
{
    private readonly IRaceQueryService _raceQueryService;
    private readonly IDataCollectionWriteService _dataCollectionWriteService;
    private readonly IJraProfileLookup _profileLookup;
    private readonly AgentAcquisitionStatusRecorder _statusRecorder;

    public JraHistoricalDataRequestHandler(
        IRaceQueryService raceQueryService,
        IDataCollectionWriteService dataCollectionWriteService,
        IJraProfileLookup profileLookup,
        AgentAcquisitionStatusRecorder statusRecorder)
    {
        _raceQueryService = raceQueryService;
        _dataCollectionWriteService = dataCollectionWriteService;
        _profileLookup = profileLookup;
        _statusRecorder = statusRecorder;
    }

    public string ProviderType => "JRA";

    public async Task<HistoricalDataRequestExecutionResult> HandleHorseHistoryRequestAsync(
        HorseHistoryCollectionRequestPayload payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            HorseReadModel? horse = await _raceQueryService
                .GetHorseAsync(payload.HorseId, cancellationToken)
                .ConfigureAwait(false);

            if (horse is null || string.IsNullOrWhiteSpace(horse.RegisteredName))
            {
                return HistoricalDataRequestExecutionResult.PermanentFailure(
                    $"Horse profile seed data was not found via API. HorseId={payload.HorseId}");
            }

            JraExtractionEnvelope<JraHorseProfileData> extraction = await _profileLookup
                .GetHorseProfileWithHistoryAsync(horse.RegisteredName, cancellationToken)
                .ConfigureAwait(false);

            if (!extraction.Success || extraction.Data is null)
            {
                return HistoricalDataRequestExecutionResult.Retry(
                    $"Failed to fetch JRA horse profile. HorseId={payload.HorseId}, Error={extraction.Error ?? "unknown"}");
            }

            JraEntityProfile profile = extraction.Data.Profile;
            var horseName = profile.DisplayName ?? horse.RegisteredName;
            var sexCode = profile.SexCode ?? horse.SexCode;

            await _dataCollectionWriteService.UpsertHorseWithOwnerAsync(
                horseName,
                horse.NormalizedName,
                sexCode,
                FormatDate(profile.BirthDate ?? horse.BirthDate),
                profile.OwnerName ?? horse.OwnerName,
                cancellationToken).ConfigureAwait(false);

            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Horse,
                AgentAcquisitionOperationType.ProfileSync,
                horseName,
                RaceDataCollectionState.Succeeded,
                ProviderType,
                payload.HorseId,
                payload.RequestedByRaceId,
                extraction.SourceUrl,
                errorCode: null,
                errorReason: null,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(profile.OwnerName))
            {
                await _statusRecorder.RecordAsync(
                    AgentAcquisitionSubjectType.Owner,
                    AgentAcquisitionOperationType.ProfileSync,
                    profile.OwnerName,
                    RaceDataCollectionState.Succeeded,
                    ProviderType,
                    DeterministicIdGenerator.BuildEntityId("owner", profile.OwnerName),
                    payload.RequestedByRaceId,
                    extraction.SourceUrl,
                    errorCode: null,
                    errorReason: null,
                    cancellationToken).ConfigureAwait(false);
            }

            var persistedCount = await PersistRaceHistoryAsync(
                horseName, sexCode, extraction.Data.RaceHistory, cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Success(
                $"JRA horse profile synchronized for HorseId={payload.HorseId}. RaceHistoryEntriesPersisted={persistedCount}/{extraction.Data.RaceHistory.Count}.");
        }
        catch (Exception ex)
        {
            var error = RaceDataCollectionErrorClassifier.Classify(ex.Message, ex);
            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Horse,
                AgentAcquisitionOperationType.ProfileSync,
                payload.HorseId,
                RaceDataCollectionState.Failed,
                ProviderType,
                payload.HorseId,
                payload.RequestedByRaceId,
                sourceUrl: null,
                error.Code,
                error.Reason,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Retry(
                $"Horse history synchronization failed for HorseId={payload.HorseId}. {ex.Message}");
        }
    }

    public async Task<HistoricalDataRequestExecutionResult> HandleJockeyHistoryRequestAsync(
        JockeyHistoryCollectionRequestPayload payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            JockeyReadModel? jockey = await _raceQueryService
                .GetJockeyAsync(payload.JockeyId, cancellationToken)
                .ConfigureAwait(false);

            if (jockey is null || string.IsNullOrWhiteSpace(jockey.DisplayName))
            {
                return HistoricalDataRequestExecutionResult.PermanentFailure(
                    $"Jockey profile seed data was not found via API. JockeyId={payload.JockeyId}");
            }

            JraExtractionEnvelope<JraEntityProfile> extraction = await _profileLookup
                .GetJockeyProfileAsync(jockey.DisplayName, cancellationToken)
                .ConfigureAwait(false);

            if (!extraction.Success || extraction.Data is null)
            {
                return HistoricalDataRequestExecutionResult.Retry(
                    $"Failed to fetch JRA jockey profile. JockeyId={payload.JockeyId}, Error={extraction.Error ?? "unknown"}");
            }

            JraEntityProfile profile = extraction.Data;
            await _dataCollectionWriteService.UpsertJockeyAsync(
                profile.DisplayName ?? jockey.DisplayName,
                jockey.NormalizedName,
                profile.Affiliation ?? jockey.AffiliationCode,
                cancellationToken).ConfigureAwait(false);

            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Jockey,
                AgentAcquisitionOperationType.ProfileSync,
                profile.DisplayName ?? jockey.DisplayName,
                RaceDataCollectionState.Succeeded,
                ProviderType,
                payload.JockeyId,
                payload.RequestedByRaceId,
                extraction.SourceUrl,
                errorCode: null,
                errorReason: null,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Success(
                $"JRA jockey profile synchronized for JockeyId={payload.JockeyId}.");
        }
        catch (Exception ex)
        {
            var error = RaceDataCollectionErrorClassifier.Classify(ex.Message, ex);
            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Jockey,
                AgentAcquisitionOperationType.ProfileSync,
                payload.JockeyId,
                RaceDataCollectionState.Failed,
                ProviderType,
                payload.JockeyId,
                payload.RequestedByRaceId,
                sourceUrl: null,
                error.Code,
                error.Reason,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Retry(
                $"Jockey history synchronization failed for JockeyId={payload.JockeyId}. {ex.Message}");
        }
    }

    public async Task<HistoricalDataRequestExecutionResult> HandleTrainerProfileRequestAsync(
        TrainerProfileCollectionRequestPayload payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TrainerReadModel? trainer = await _raceQueryService
                .GetTrainerAsync(payload.TrainerId, cancellationToken)
                .ConfigureAwait(false);

            if (trainer is null || string.IsNullOrWhiteSpace(trainer.DisplayName))
            {
                return HistoricalDataRequestExecutionResult.PermanentFailure(
                    $"Trainer profile seed data was not found via API. TrainerId={payload.TrainerId}");
            }

            JraExtractionEnvelope<JraEntityProfile> extraction = await _profileLookup
                .GetTrainerProfileAsync(trainer.DisplayName, cancellationToken)
                .ConfigureAwait(false);

            if (!extraction.Success || extraction.Data is null)
            {
                return HistoricalDataRequestExecutionResult.Retry(
                    $"Failed to fetch JRA trainer profile. TrainerId={payload.TrainerId}, Error={extraction.Error ?? "unknown"}");
            }

            var profile = extraction.Data;
            var trainerName = profile.DisplayName ?? trainer.DisplayName;
            await _dataCollectionWriteService.UpsertTrainerAsync(
                trainerName,
                trainer.NormalizedName,
                profile.Affiliation ?? trainer.AffiliationCode,
                cancellationToken).ConfigureAwait(false);

            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Trainer,
                AgentAcquisitionOperationType.ProfileSync,
                trainerName,
                RaceDataCollectionState.Succeeded,
                ProviderType,
                payload.TrainerId,
                payload.RequestedByRaceId,
                extraction.SourceUrl,
                errorCode: null,
                errorReason: null,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Success(
                $"JRA trainer profile synchronized for TrainerId={payload.TrainerId}.");
        }
        catch (Exception ex)
        {
            var error = RaceDataCollectionErrorClassifier.Classify(ex.Message, ex);
            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Trainer,
                AgentAcquisitionOperationType.ProfileSync,
                payload.TrainerId,
                RaceDataCollectionState.Failed,
                ProviderType,
                payload.TrainerId,
                payload.RequestedByRaceId,
                sourceUrl: null,
                error.Code,
                error.Reason,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.Retry(
                $"Trainer profile synchronization failed for TrainerId={payload.TrainerId}. {ex.Message}");
        }
    }

    /// <summary>
    /// 競走馬情報ページから抽出した過去の競走成績を、レース・出走・結果として Api へ登録する。
    /// 1件のレースが登録に必要な情報（開催日・競馬場・R・レース名・馬番）を欠く場合はスキップし、
    /// 個々のレースの登録失敗が他のレースの登録を止めないようにする。
    /// </summary>
    private async Task<int> PersistRaceHistoryAsync(
        string horseName,
        string? sexCode,
        IReadOnlyList<JraHorseRaceHistoryEntryData> raceHistory,
        CancellationToken cancellationToken)
    {
        var persistedCount = 0;

        foreach (var entry in raceHistory)
        {
            if (entry.RaceDate is null
                || string.IsNullOrWhiteSpace(entry.Racecourse)
                || entry.RaceNumber is not > 0
                || string.IsNullOrWhiteSpace(entry.RaceName)
                || entry.HorseNumber is not > 0)
            {
                continue;
            }

            try
            {
                var raceId = await _dataCollectionWriteService.UpsertRaceAsync(
                    entry.RaceDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    entry.Racecourse,
                    entry.RaceNumber.Value,
                    entry.RaceName,
                    entryCount: null,
                    gradeCode: null,
                    entry.SurfaceCode,
                    entry.DistanceMeters,
                    directionCode: null,
                    cancellationToken).ConfigureAwait(false);

                await _dataCollectionWriteService.UpsertRaceEntryAsync(
                    raceId,
                    entry.HorseNumber.Value,
                    horseName,
                    entry.JockeyName,
                    trainerName: null,
                    entry.GateNumber,
                    entry.AssignedWeight,
                    sexCode,
                    age: null,
                    entry.BodyWeight,
                    entry.BodyWeightDiff,
                    cancellationToken).ConfigureAwait(false);

                var winningHorseName = entry.FinishPosition == 1 ? horseName : entry.WinnerOrRunnerUpHorseName;
                if (!string.IsNullOrWhiteSpace(winningHorseName))
                {
                    await _dataCollectionWriteService.DeclareRaceResultAsync(
                        raceId,
                        winningHorseName,
                        declaredAt: null,
                        winningHorseId: null,
                        cancellationToken).ConfigureAwait(false);
                }

                await _dataCollectionWriteService.DeclareRaceEntryResultAsync(
                    raceId,
                    entry.HorseNumber.Value,
                    entry.FinishPosition,
                    entry.OfficialTime,
                    entry.MarginText,
                    entry.LastThreeFurlongTime,
                    entry.AbnormalResultCode,
                    entry.PrizeMoney,
                    cancellationToken).ConfigureAwait(false);

                persistedCount++;
            }
            catch (Exception)
            {
                // 1走分の登録に失敗しても、他のレースの登録は継続する。
            }
        }

        return persistedCount;
    }

    private static string? FormatDate(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
