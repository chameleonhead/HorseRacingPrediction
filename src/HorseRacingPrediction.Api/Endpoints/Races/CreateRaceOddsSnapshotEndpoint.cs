using EventFlow;
using EventFlow.Queries;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Domain.Races;
using Microsoft.AspNetCore.Mvc;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class CreateRaceOddsSnapshotEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v2/admin/races/{raceId}/odds-snapshot-records",
            async (string raceId, RecordRaceOddsSnapshotRequest request, [FromServices] ICommandBus commands,
                [FromServices] IQueryProcessor queries, CancellationToken token) =>
            {
                var entries = request.Entries ?? [];
                var observations = request.Observations;
                var errors = RaceOddsEndpointMappings.Validate(request.ObservedAt, entries, observations);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                var race = await queries.ProcessAsync(
                    new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId), token);
                if (race is null) return Results.NotFound();
                var activeEntries = race.Entries
                    .Where(entry => entry.ParticipationStatus == HorseRacingPrediction.Domain.Races.RaceEntryParticipationStatus.Active).ToArray();
                if (activeEntries.Length == 0 || activeEntries.Any(entry => entry.HorseNumber is null or <= 0))
                    return Results.Conflict(new { ErrorCode = "RaceAssignmentNotConfirmed", Message = "Confirmed horse assignments are required for odds." });
                if (entries.Any(entry => !activeEntries.Any(assignment => assignment.HorseNumber == entry.HorseNumber)))
                    return Results.BadRequest(new { ErrorCode = "UnknownHorseNumber", Message = "Odds reference an unknown horse number." });
                var assignments = activeEntries.Select(entry => new RaceOddsAssignment(entry.HorseNumber!.Value,
                    entry.HorseId, entry.EntryId, entry.GateNumber)).ToArray();
                if (observations?.Any(item => !RaceOddsSelection.CanResolve(
                        new RaceOddsObservation(item.Market, item.Selection, item.Value), assignments)) == true)
                    return Results.BadRequest(new { ErrorCode = "UnknownHorseNumber", Message = "Odds selections must resolve to confirmed horse or frame assignments." });
                await commands.PublishAsync(new RecordRaceOddsSnapshotCommand(new RaceId(raceId), request.ObservedAt,
                    entries.Select(x => new RaceOddsEntry(x.HorseNumber, x.WinOdds, x.Popularity)).ToArray(),
                    observations?.Select(x => new RaceOddsObservation(x.Market, x.Selection, x.Value,
                        x.Popularity)).ToArray()), token);
                return Results.Created($"/api/v2/admin/races/{Uri.EscapeDataString(raceId)}/odds-snapshot-records", null);
            })
            .AddEndpointFilter<RaceWriteEndpointFilter>()
            .AddEndpointFilter<RaceActiveCollectionEndpointFilter>();
    }
}
