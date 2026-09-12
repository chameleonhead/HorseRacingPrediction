using EventFlow;
using EventFlow.Queries;
using EventFlow.ReadStores;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Api.CollectionController;

public static class RaceOddsEndpointExtensions
{
    public static IEndpointRouteBuilder MapRaceOddsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/races/{raceId}/odds-snapshots", async (string raceId,
            RecordRaceOddsSnapshotRequest request, ICommandBus commands, CancellationToken token) =>
        {
            await commands.PublishAsync(new RecordRaceOddsSnapshotCommand(new RaceId(raceId), request.ObservedAt,
                request.Entries.Select(x => new RaceOddsEntry(x.HorseNumber, x.WinOdds, x.Popularity)).ToArray(),
                request.Observations?.Select(x => new RaceOddsObservation(x.Market, x.Selection, x.Value,
                    x.Popularity)).ToArray()), token);
            return Results.Accepted();
        });
        endpoints.MapGet("/api/admin/races/{raceId}/odds-snapshots", async (string raceId,
            IQueryProcessor queries, CancellationToken token) =>
        {
            var model = await queries.ProcessAsync(new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId), token);
            return model is null ? Results.NotFound() : Results.Ok(model.OddsSnapshots);
        });
        return endpoints;
    }
}
