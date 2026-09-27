using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class ListRaceOddsSnapshotsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/races/{raceId}/odds-snapshot-records",
            async (string raceId, IQueryProcessor queries, CancellationToken token) =>
            {
                var model = await queries.ProcessAsync(
                    new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId), token);
                return model is null ? Results.NotFound() : Results.Ok(model.OddsSnapshots);
            });
}
