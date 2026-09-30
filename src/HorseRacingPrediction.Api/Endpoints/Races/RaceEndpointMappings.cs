using EventFlow;
using EventFlow.EntityFramework;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Queries.ReadModels;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class RaceEndpointMappings
{
    internal static async Task EnsureRelatedSubjectsAsync(
        RegisterEntryRequest request,
        ICommandBus commandBus,
        IDbContextProvider<EventStoreDbContext> dbContextProvider,
        CancellationToken cancellationToken)
    {
        using var dbContext = dbContextProvider.CreateContext();

        var horseExists = await dbContext.Set<AppReadModels.HorseReadModel>()
            .AsNoTracking()
            .AnyAsync(x => x.HorseId == request.HorseId, cancellationToken)
            .ConfigureAwait(false);

        if (!horseExists)
        {
            var horseName = string.IsNullOrWhiteSpace(request.HorseName) ? request.HorseId : request.HorseName;
            var normalizedHorseName = NormalizeDisplayName(horseName);
            var registerHorse = new RegisterHorseCommand(
                new HorseId(request.HorseId),
                horseName,
                normalizedHorseName,
                request.SexCode,
                birthDate: null);

            var horseResult = await commandBus.PublishAsync(registerHorse, cancellationToken).ConfigureAwait(false);
            if (!horseResult.IsSuccess)
            {
                throw new InvalidOperationException($"Horse auto-registration failed. HorseId={request.HorseId}");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.JockeyId))
        {
            var jockeyExists = await dbContext.Set<AppReadModels.JockeyReadModel>()
                .AsNoTracking()
                .AnyAsync(x => x.JockeyId == request.JockeyId, cancellationToken)
                .ConfigureAwait(false);

            if (!jockeyExists)
            {
                var jockeyName = string.IsNullOrWhiteSpace(request.JockeyName) ? request.JockeyId : request.JockeyName;
                var normalizedJockeyName = NormalizeDisplayName(jockeyName);
                var registerJockey = new RegisterJockeyCommand(
                    new JockeyId(request.JockeyId),
                    jockeyName,
                    normalizedJockeyName,
                    affiliationCode: null);

                var jockeyResult = await commandBus.PublishAsync(registerJockey, cancellationToken).ConfigureAwait(false);
                if (!jockeyResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Jockey auto-registration failed. JockeyId={request.JockeyId}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(request.TrainerId))
        {
            var trainerExists = await dbContext.Set<TrainerReadModel>()
                .AsNoTracking()
                .AnyAsync(x => x.TrainerId == request.TrainerId, cancellationToken)
                .ConfigureAwait(false);

            if (!trainerExists)
            {
                var trainerName = string.IsNullOrWhiteSpace(request.TrainerName) ? request.TrainerId : request.TrainerName;
                var normalizedTrainerName = NormalizeDisplayName(trainerName);
                var registerTrainer = new RegisterTrainerCommand(
                    new TrainerId(request.TrainerId),
                    trainerName,
                    normalizedTrainerName,
                    affiliationCode: null);

                var trainerResult = await commandBus.PublishAsync(registerTrainer, cancellationToken).ConfigureAwait(false);
                if (!trainerResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Trainer auto-registration failed. TrainerId={request.TrainerId}");
                }
            }
        }
    }

    internal static string NormalizeDisplayName(string value)
        => string.Join(
            string.Empty,
            value
                .Trim()
                .Where(c => !char.IsWhiteSpace(c)));

    internal static ApiContracts.RacePredictionContextDto ToAgentRacePredictionContext(HorseRacingPrediction.Application.Queries.ReadModels.RacePredictionContextReadModel model)
        => new()
        {
            RaceId = model.RaceId,
            RaceDate = model.RaceDate,
            RacecourseCode = model.RacecourseCode,
            RaceNumber = model.RaceNumber,
            RaceName = model.RaceName,
            Status = (ApiContracts.RaceStatus)(int)model.Status,
            GradeCode = model.GradeCode,
            SurfaceCode = model.SurfaceCode,
            DistanceMeters = model.DistanceMeters,
            DirectionCode = model.DirectionCode,
            Entries = model.Entries.Select(x => new ApiContracts.RacePredictionContextEntryDto(x.EntryId, x.HorseId, x.HorseNumber, x.JockeyId, x.TrainerId, x.GateNumber, x.AssignedWeight, x.SexCode, x.Age, x.DeclaredWeight, x.DeclaredWeightDiff, x.RunningStyleCode, x.OwnerName, (ApiContracts.RaceEntryParticipationStatus)x.ParticipationStatus)).ToList(),
            WeatherObservations = model.WeatherObservations.Select(x => new ApiContracts.WeatherObservationSnapshot(x.ObservationTime, x.WeatherCode, x.WeatherText, x.TemperatureCelsius, x.HumidityPercent, x.WindDirectionCode, x.WindSpeedMeterPerSecond)).ToList(),
            TrackConditionObservations = model.TrackConditionObservations.Select(x => new ApiContracts.TrackConditionSnapshot(x.ObservationTime, x.TurfConditionCode, x.DirtConditionCode, x.GoingDescriptionText)).ToList()
        };

    internal static RaceEntryDto ToRaceEntryResponse(AppReadModels.EntryResultSnapshot entryResult, string? horseId, int? horseNumber, int? gateNumber, string? horseName, string? ownerName, string? ownerId)
        => new(
            entryResult.EntryId,
            horseId ?? string.Empty,
            horseName,
            horseNumber,
            null,
            null,
            null,
            null,
            gateNumber,
            null,
            null,
            null,
            null,
            null,
            null,
            ownerName,
            ownerId);

    internal static RaceEntryDto ToRaceEntryResponse(
        HorseRacingPrediction.Application.Queries.ReadModels.RacePredictionContextEntry entry,
        string? horseName,
        string? jockeyName,
        string? trainerName,
        int? gateNumber,
        string? ownerName,
        string? ownerId)
        => new(
            entry.EntryId,
            entry.HorseId,
            horseName,
            entry.HorseNumber,
            entry.JockeyId,
            jockeyName,
            entry.TrainerId,
            trainerName,
            gateNumber,
            entry.AssignedWeight,
            entry.SexCode,
            entry.Age,
            entry.DeclaredWeight,
            entry.DeclaredWeightDiff,
            entry.RunningStyleCode,
            ownerName,
            ownerId,
            (ApiContracts.RaceEntryParticipationStatus)entry.ParticipationStatus);

    internal static RaceWeatherObservationDto ToRaceWeatherObservationResponse(
        HorseRacingPrediction.Application.Queries.ReadModels.WeatherObservationSnapshot observation)
        => new(
            observation.ObservationTime,
            observation.WeatherCode,
            observation.WeatherText,
            observation.TemperatureCelsius,
            observation.HumidityPercent,
            observation.WindDirectionCode,
            observation.WindSpeedMeterPerSecond);

    internal static RaceTrackConditionDto ToRaceTrackConditionResponse(
        HorseRacingPrediction.Application.Queries.ReadModels.TrackConditionSnapshot condition)
        => new(
            condition.ObservationTime,
            condition.TurfConditionCode,
            condition.DirtConditionCode,
            condition.GoingDescriptionText);

    internal static RaceEntryResultDto ToRaceEntryResultResponse(AppReadModels.EntryResultSnapshot entryResult, string? horseId, int? horseNumber, string? horseName)
        => new(
            entryResult.EntryId,
            horseId ?? string.Empty,
            horseName,
            horseNumber,
            entryResult.FinishPosition,
            entryResult.OfficialTime,
            entryResult.MarginText,
            entryResult.LastThreeFurlongTime,
            entryResult.AbnormalResultCode,
            entryResult.PrizeMoney,
            entryResult.CornerPositions, entryResult.Popularity, entryResult.OriginalFinishPosition, entryResult.IsDeadHeat, entryResult.Average1F,
            entryResult.AdditionalPrizeMoney);

    internal static string? ResolveHorseId(IReadOnlyDictionary<string, string> entryHorseIdsByEntryId, string entryId, string? horseId)
        => !string.IsNullOrWhiteSpace(horseId)
            ? horseId
            : entryHorseIdsByEntryId.TryGetValue(entryId, out var fallbackHorseId)
                ? fallbackHorseId
                : null;

    internal static int? ResolveHorseNumber(IReadOnlyDictionary<string, int?> entryHorseNumbersByEntryId, string entryId, int? horseNumber)
        => entryHorseNumbersByEntryId.TryGetValue(entryId, out var currentHorseNumber) ? currentHorseNumber : horseNumber;

    internal static int? ResolveGateNumber(
        IReadOnlyDictionary<string, int> entryGateNumbersByEntryId,
        IReadOnlyDictionary<string, int> resultEntryGateNumbersByEntryId,
        string entryId,
        int? horseNumber)
        => entryGateNumbersByEntryId.TryGetValue(entryId, out var gateNumber)
            ? gateNumber
            : resultEntryGateNumbersByEntryId.TryGetValue(entryId, out var fallbackGateNumber)
                ? fallbackGateNumber
                : null;

    internal static string? ResolveHorseName(IReadOnlyDictionary<string, string> horseNamesById, string? horseId)
        => !string.IsNullOrWhiteSpace(horseId) && horseNamesById.TryGetValue(horseId, out var horseName)
            ? horseName
            : null;

    internal static string? ResolveJockeyName(IReadOnlyDictionary<string, string> jockeyNamesById, string? jockeyId)
        => !string.IsNullOrWhiteSpace(jockeyId) && jockeyNamesById.TryGetValue(jockeyId, out var jockeyName)
            ? jockeyName
            : null;

    internal static string? ResolveTrainerName(IReadOnlyDictionary<string, string> trainerNamesById, string? trainerId)
        => !string.IsNullOrWhiteSpace(trainerId) && trainerNamesById.TryGetValue(trainerId, out var trainerName)
            ? trainerName
            : null;

    internal static RacePayoutResultDto ToRacePayoutResultResponse(AppReadModels.PayoutResultSnapshot payoutResult)
        => new(
            payoutResult.DeclaredAt,
            payoutResult.WinPayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.PlacePayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.QuinellaPayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.ExactaPayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.TrifectaPayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.BracketQuinellaPayouts?.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.WidePayouts?.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.TrioPayouts?.Select(ToRacePayoutEntryResponse).ToList());

    internal static RacePayoutEntryDto ToRacePayoutEntryResponse(AppReadModels.PayoutEntrySnapshot payout)
        => new(payout.Combination, payout.Amount);

    internal static RaceOddsDto BuildUnavailableOddsResponse()
        => new(
            false,
            "保存済みオッズはまだ API ReadModel に保持されていません。",
            [],
            []);
}
