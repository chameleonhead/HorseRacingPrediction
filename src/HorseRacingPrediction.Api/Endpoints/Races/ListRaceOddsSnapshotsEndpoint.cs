using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Contracts.Races;
using Microsoft.AspNetCore.Mvc;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class ListRaceOddsSnapshotsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/races/{raceId}/odds-snapshot-records",
            async (string raceId, [FromServices] IQueryProcessor queries, CancellationToken token) =>
            {
                var model = await queries.ProcessAsync(
                    new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId), token);
                return model is null ? Results.NotFound() : Results.Ok(new ListRaceOddsSnapshotsResponse(
                    model.OddsSnapshots.Select(snapshot => new RaceOddsSnapshotDto(snapshot.ObservedAt,
                        snapshot.Entries.Select(entry => new RaceOddsEntrySnapshotDto(entry.HorseNumber,
                            entry.WinOdds, entry.Popularity)).ToArray(),
                        snapshot.Observations?.Select(observation => new RaceOddsObservationSnapshotDto(
                            observation.Market, observation.Selection, observation.Value, observation.Popularity)).ToArray(),
                        snapshot.Assignments?.Select(assignment => new RaceOddsAssignmentDto(assignment.HorseNumber,
                            assignment.HorseId, assignment.EntryId, assignment.GateNumber)).ToArray())).ToArray()));
            })
            .Produces<ListRaceOddsSnapshotsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
}
