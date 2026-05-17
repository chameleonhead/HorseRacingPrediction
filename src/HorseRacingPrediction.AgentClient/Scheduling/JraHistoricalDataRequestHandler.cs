using System.Globalization;
using HorseRacingPrediction.Agents.Contracts;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;

namespace HorseRacingPrediction.AgentClient.Scheduling;

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

            JraExtractionEnvelope<JraEntityProfile> extraction = await _profileLookup
                .GetHorseProfileAsync(horse.RegisteredName, cancellationToken)
                .ConfigureAwait(false);

            if (!extraction.Success || extraction.Data is null)
            {
                return HistoricalDataRequestExecutionResult.Retry(
                    $"Failed to fetch JRA horse profile. HorseId={payload.HorseId}, Error={extraction.Error ?? "unknown"}");
            }

            JraEntityProfile profile = extraction.Data;
            await _dataCollectionWriteService.UpsertHorseAsync(
                profile.DisplayName ?? horse.RegisteredName,
                horse.NormalizedName,
                profile.SexCode ?? horse.SexCode,
                FormatDate(profile.BirthDate ?? horse.BirthDate),
                cancellationToken).ConfigureAwait(false);

            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Horse,
                AgentAcquisitionOperationType.ProfileSync,
                profile.DisplayName ?? horse.RegisteredName,
                RaceDataCollectionState.Succeeded,
                ProviderType,
                payload.HorseId,
                payload.RequestedByRaceId,
                extraction.SourceUrl,
                errorCode: null,
                errorReason: null,
                cancellationToken).ConfigureAwait(false);

            return HistoricalDataRequestExecutionResult.PermanentFailure(
                $"JRA horse profile synchronized for HorseId={payload.HorseId}, but structured horse race history persistence is not implemented yet.");
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

            return HistoricalDataRequestExecutionResult.PermanentFailure(
                $"JRA jockey profile synchronized for JockeyId={payload.JockeyId}, but structured jockey race history persistence is not implemented yet.");
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

    private static string? FormatDate(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}