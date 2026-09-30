using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;

using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class TrainerEndpointMappings
{
    internal static IOrderedEnumerable<AppReadModels.TrainerReadModel>? SortTrainers(
        IEnumerable<AppReadModels.TrainerReadModel> source,
        SearchTrainersRequest request)
        => (request.SortBy ?? "displayName").ToLowerInvariant() switch
        {
            "displayname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.DisplayName).ThenByDescending(x => x.TrainerId)
                : source.OrderBy(x => x.DisplayName).ThenBy(x => x.TrainerId),
            "normalizedname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.NormalizedName).ThenByDescending(x => x.TrainerId)
                : source.OrderBy(x => x.NormalizedName).ThenBy(x => x.TrainerId),
            "affiliationcode" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.AffiliationCode).ThenByDescending(x => x.DisplayName)
                : source.OrderBy(x => x.AffiliationCode).ThenBy(x => x.DisplayName),
            _ => null
        };
}
