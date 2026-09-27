using EventFlow;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class CreateRaceFromScheduleEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v2/admin/races", async (PrepareHorseHistoryRaceRequest request,
            IDbContextProvider<EventStoreDbContext> provider, ICommandBus commands, CancellationToken token) =>
        {
            var course = RaceCourseIdentity.Canonicalize(request.Course);
            if (course is null || request.RaceNumber is < 1 or > 12
                || string.IsNullOrWhiteSpace(request.RaceName))
                return Results.BadRequest();
            using var db = provider.CreateContext();
            string id;
            try
            {
                id = await CollectionIdentityResolver.RaceAsync(db, request.RaceDate, course,
                    request.RaceNumber, token);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { code = exception.Message });
            }

            var existing = await db.RacePredictionContexts.AsNoTracking()
                .AnyAsync(x => x.RaceId == id, token);
            if (existing) return Results.Ok(new { raceId = id });
            try
            {
                await commands.PublishAsync(new CreateRaceCommand(new RaceId(id), request.RaceDate,
                    course, request.RaceNumber, request.RaceName), token);
            }
            catch (InvalidOperationException exception)
                when (exception.Message == "Race is already created.")
            {
                return Results.Ok(new { raceId = id });
            }
            return Results.Created($"/api/v2/admin/races/{Uri.EscapeDataString(id)}", new { raceId = id });
        }).AddEndpointFilter<RaceWriteEndpointFilter>();
    }
}
