using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class HorseEndpointMappings
{
    internal static IOrderedEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel>? SortHorses(
        IEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel> source,
        SearchHorsesRequest request)
        => (request.SortBy ?? "registeredName").ToLowerInvariant() switch
        {
            "registeredname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.RegisteredName).ThenByDescending(x => x.HorseId)
                : source.OrderBy(x => x.RegisteredName).ThenBy(x => x.HorseId),
            "normalizedname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.NormalizedName).ThenByDescending(x => x.HorseId)
                : source.OrderBy(x => x.NormalizedName).ThenBy(x => x.HorseId),
            "birthdate" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.BirthDate).ThenByDescending(x => x.RegisteredName)
                : source.OrderBy(x => x.BirthDate).ThenBy(x => x.RegisteredName),
            _ => null
        };

    internal static ApiContracts.HorseDto ToAgentHorse(HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel model)
        => new()
        {
            HorseId = model.HorseId,
            RegisteredName = model.RegisteredName,
            NormalizedName = model.NormalizedName,
            SexCode = model.SexCode,
            BirthDate = model.BirthDate,
            OwnerName = model.OwnerName,
            BreederName = model.BreederName,
            SireName = model.SireName,
            DamName = model.DamName,
            DamsireName = model.DamsireName,
            CoatColor = model.CoatColor,
            Aliases = model.Aliases.Select(x => new ApiContracts.HorseAliasEntry(x.AliasType, x.AliasValue, x.SourceName, x.IsPrimary)).ToList()
        };

    internal static ApiContracts.HorseRaceHistoryDto ToAgentHorseRaceHistory(HorseRacingPrediction.Application.Queries.ReadModels.HorseRaceHistoryReadModel model)
        => new()
        {
            HorseId = model.HorseId,
            Entries = model.Entries.Select(x => new ApiContracts.HorseRaceHistoryEntryDto(x.RaceId, x.EntryId, x.RaceDate, x.RacecourseCode, x.SurfaceCode, x.DistanceMeters, x.DirectionCode, x.GradeCode, x.GateNumber, x.AssignedWeight, x.DeclaredWeight, x.DeclaredWeightDiff, x.RunningStyleCode, x.JockeyId, x.TrainerId, x.FinishPosition, x.LastThreeFurlongTime, x.CornerPositions, x.PrizeMoney)).ToList()
        };
}
