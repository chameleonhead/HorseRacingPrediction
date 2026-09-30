using EventFlow.EntityFramework;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Queries.ReadModels;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

using static HorseRacingPrediction.Api.Endpoints.Races.RaceEndpointMappings;
using static HorseRacingPrediction.Api.Endpoints.Owners.OwnerEndpointService;


internal static class GetRaceEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/races/{raceId}",
                    [SwaggerOperation(Summary = "Get race", Description = "Returns race read model with current status and result information")]
        async (string raceId, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        using var dbContext = dbContextProvider.CreateContext();
                        var readModel = await dbContext.Set<AppReadModels.RacePredictionContextReadModel>()
                            .AsNoTracking()
                            .SingleOrDefaultAsync(x => x.RaceId == raceId, cancellationToken)
                            .ConfigureAwait(false);

                        var resultReadModel = await dbContext.Set<RaceResultViewReadModel>()
                            .AsNoTracking()
                            .SingleOrDefaultAsync(x => x.RaceId == raceId, cancellationToken)
                            .ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                            return Results.NotFound();

                        var entryHorseIdsByEntryId = readModel.Entries
                            .Where(x => !string.IsNullOrWhiteSpace(x.EntryId) && !string.IsNullOrWhiteSpace(x.HorseId))
                            .ToDictionary(x => x.EntryId, x => x.HorseId, StringComparer.Ordinal);

                        var entryHorseNumbersByEntryId = readModel.Entries
                            .Where(x => !string.IsNullOrWhiteSpace(x.EntryId))
                            .ToDictionary(x => x.EntryId, x => x.HorseNumber, StringComparer.Ordinal);

                        var entryGateNumbersByEntryId = readModel.Entries
                            .Where(x => !string.IsNullOrWhiteSpace(x.EntryId) && x.GateNumber.HasValue)
                            .ToDictionary(x => x.EntryId, x => x.GateNumber!.Value, StringComparer.Ordinal);

                        var resultEntryGateNumbersByEntryId = resultReadModel?.EntryIndexes
                            .Where(x => !string.IsNullOrWhiteSpace(x.EntryId) && x.GateNumber.HasValue)
                            .ToDictionary(x => x.EntryId, x => x.GateNumber!.Value, StringComparer.Ordinal)
                            ?? new Dictionary<string, int>(StringComparer.Ordinal);

                        var horseIds = readModel.Entries
                            .Select(x => x.HorseId)
                            .Concat(resultReadModel?.EntryResults.Select(x => ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)) ?? [])
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                        List<AppReadModels.HorseReadModel> horseProfiles = horseIds.Count == 0
                            ? []
                            : await dbContext.Set<AppReadModels.HorseReadModel>()
                                .AsNoTracking()
                                .Where(x => horseIds.Contains(x.HorseId))
                                .ToListAsync(cancellationToken)
                                .ConfigureAwait(false);
                        var horseNamesById = horseProfiles
                            .ToDictionary(x => x.HorseId, x => x.RegisteredName, StringComparer.Ordinal);
                        var ownerNamesByHorseId = horseProfiles
                            .Where(x => !string.IsNullOrWhiteSpace(x.OwnerName))
                            .ToDictionary(x => x.HorseId, x => x.OwnerName!, StringComparer.Ordinal);

                        var jockeyIds = readModel.Entries
                            .Select(x => x.JockeyId)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                        var trainerIds = readModel.Entries
                            .Select(x => x.TrainerId)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                        var jockeyNamesById = jockeyIds.Count == 0
                            ? new Dictionary<string, string>(StringComparer.Ordinal)
                            : await dbContext.Set<AppReadModels.JockeyReadModel>()
                                .AsNoTracking()
                                .Where(x => jockeyIds.Contains(x.JockeyId))
                                .ToDictionaryAsync(x => x.JockeyId, x => x.DisplayName, StringComparer.Ordinal, cancellationToken)
                                .ConfigureAwait(false);

                        var trainerNamesById = trainerIds.Count == 0
                            ? new Dictionary<string, string>(StringComparer.Ordinal)
                            : await dbContext.Set<TrainerReadModel>()
                                .AsNoTracking()
                                .Where(x => trainerIds.Contains(x.TrainerId))
                                .ToDictionaryAsync(x => x.TrainerId, x => x.DisplayName, StringComparer.Ordinal, cancellationToken)
                                .ConfigureAwait(false);
                        var ownerMappings = await dbContext.OwnerAliasMappings.AsNoTracking()
                            .ToDictionaryAsync(x => x.NormalizedAlias, x => x.OwnerId, cancellationToken).ConfigureAwait(false);

                        var entryResponses = readModel.Entries.Count > 0
                            ? readModel.Entries.Select(x => ToRaceEntryResponse(
                                x,
                                ResolveHorseName(horseNamesById, x.HorseId),
                                ResolveJockeyName(jockeyNamesById, x.JockeyId),
                                ResolveTrainerName(trainerNamesById, x.TrainerId),
                                ResolveGateNumber(entryGateNumbersByEntryId, resultEntryGateNumbersByEntryId, x.EntryId, x.HorseNumber),
                                x.OwnerName ?? ResolveOwnerName(ownerNamesByHorseId, x.HorseId),
                                ResolveOwnerId(x.OwnerName ?? ResolveOwnerName(ownerNamesByHorseId, x.HorseId), ownerMappings))).ToList()
                            : resultReadModel?.EntryResults.Select(x => ToRaceEntryResponse(
                                x,
                                ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId),
                                ResolveHorseNumber(entryHorseNumbersByEntryId, x.EntryId, x.HorseNumber),
                                ResolveGateNumber(entryGateNumbersByEntryId, resultEntryGateNumbersByEntryId, x.EntryId, x.HorseNumber),
                                ResolveHorseName(horseNamesById, ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)),
                                ResolveOwnerName(ownerNamesByHorseId, ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)),
                                ResolveOwnerId(ResolveOwnerName(ownerNamesByHorseId, ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)), ownerMappings))).ToList() ?? [];

                        var winningHorseId = resultReadModel?.WinningHorseId;
                        if (string.IsNullOrWhiteSpace(winningHorseId))
                        {
                            var winnerEntry = resultReadModel?.EntryResults
                                .FirstOrDefault(x => x.FinishPosition == 1);
                            if (winnerEntry is not null)
                            {
                                winningHorseId = ResolveHorseId(entryHorseIdsByEntryId, winnerEntry.EntryId, winnerEntry.HorseId);
                            }
                        }

                        var winningHorseName = resultReadModel?.WinningHorseName;
                        if (string.IsNullOrWhiteSpace(winningHorseName) && !string.IsNullOrWhiteSpace(winningHorseId))
                        {
                            winningHorseName = ResolveHorseName(horseNamesById, winningHorseId);
                        }

                        var response = new RaceDto(
                            readModel.RaceId,
                            readModel.RaceDate,
                            readModel.RacecourseCode,
                            readModel.RaceNumber,
                            readModel.RaceName,
                            (HorseRacingPrediction.Contracts.Races.RaceStatus)(int)readModel.Status,
                            null, null,
                            readModel.GradeCode,
                            readModel.SurfaceCode,
                            readModel.DistanceMeters,
                            readModel.DirectionCode,
                            resultReadModel?.EntryCount ?? readModel.Entries.Count,
                            entryResponses,
                            readModel.WeatherObservations.Select(ToRaceWeatherObservationResponse).ToList(),
                            readModel.TrackConditionObservations.Select(ToRaceTrackConditionResponse).ToList(),
                            BuildUnavailableOddsResponse(),
                            winningHorseName,
                            winningHorseId,
                            resultReadModel?.StewardReportText,
                            resultReadModel?.ResultDeclaredAt,
                            resultReadModel?.EntryResults.Select(x => ToRaceEntryResultResponse(
                                x,
                                ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId),
                                ResolveHorseNumber(entryHorseNumbersByEntryId, x.EntryId, x.HorseNumber),
                                ResolveHorseName(horseNamesById, ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)))).ToList() ?? [],
                            resultReadModel?.PayoutResult is null ? null : ToRacePayoutResultResponse(resultReadModel.PayoutResult),
                            readModel.StartTime, readModel.OverallPaceText, readModel.CornerPassagesText, readModel.CourseLayout,
                            readModel.ReplacementRaceId);

                        return Results.Ok(new GetRaceResponse(response));
                    })
                    .WithName("GetRace")
                    .WithTags("Race API")
                    .Produces<GetRaceResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
