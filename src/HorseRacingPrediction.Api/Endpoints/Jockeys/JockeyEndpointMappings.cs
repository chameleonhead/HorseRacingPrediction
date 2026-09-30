using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

internal static class JockeyEndpointMappings
{
    internal static IOrderedEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.JockeyReadModel>? SortJockeys(
        IEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.JockeyReadModel> source,
        SearchJockeysRequest request)
        => (request.SortBy ?? "displayName").ToLowerInvariant() switch
        {
            "displayname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.DisplayName).ThenByDescending(x => x.JockeyId)
                : source.OrderBy(x => x.DisplayName).ThenBy(x => x.JockeyId),
            "normalizedname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.NormalizedName).ThenByDescending(x => x.JockeyId)
                : source.OrderBy(x => x.NormalizedName).ThenBy(x => x.JockeyId),
            "affiliationcode" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.AffiliationCode).ThenByDescending(x => x.DisplayName)
                : source.OrderBy(x => x.AffiliationCode).ThenBy(x => x.DisplayName),
            _ => null
        };

    internal static ApiContracts.JockeyDto ToAgentJockey(HorseRacingPrediction.Application.Queries.ReadModels.JockeyReadModel model)
        => new()
        {
            JockeyId = model.JockeyId,
            DisplayName = model.DisplayName,
            NormalizedName = model.NormalizedName,
            AffiliationCode = model.AffiliationCode,
            Aliases = model.Aliases.Select(x => new ApiContracts.JockeyAliasEntry(x.AliasType, x.AliasValue, x.SourceName, x.IsPrimary)).ToList()
        };

    internal static ApiContracts.JockeyRaceHistoryDto ToAgentJockeyRaceHistory(HorseRacingPrediction.Application.Queries.ReadModels.JockeyRaceHistoryReadModel model)
        => new()
        {
            JockeyId = model.JockeyId,
            Entries = model.Entries.Select(x => new ApiContracts.JockeyRaceHistoryEntryDto(x.RaceId, x.EntryId, x.HorseId, x.RaceDate, x.RacecourseCode, x.SurfaceCode, x.DistanceMeters, x.DirectionCode, x.GradeCode, x.FinishPosition, x.PrizeMoney)).ToList()
        };
}
