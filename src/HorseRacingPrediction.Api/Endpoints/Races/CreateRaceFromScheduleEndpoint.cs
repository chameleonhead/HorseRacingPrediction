using EventFlow;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class CreateRaceFromScheduleEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v2/admin/races", async (CreateRaceFromScheduleRequest request,
            [FromServices] IDbContextProvider<EventStoreDbContext> provider,
            [FromServices] ICommandBus commands, CancellationToken token) =>
        {
            var schedule = request?.Schedule;
            if (schedule is null) return Results.BadRequest();
            var course = RaceCourseIdentity.Canonicalize(schedule.Course);
            if (course is null || schedule.RaceNumber is < 1 or > 12
                || string.IsNullOrWhiteSpace(schedule.RaceName))
                return Results.BadRequest();
            using var db = provider.CreateContext();
            string id;
            try
            {
                id = await CollectionIdentityResolver.RaceAsync(db, schedule.RaceDate, course,
                    schedule.RaceNumber, token);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { code = exception.Message });
            }

            var existing = await db.RacePredictionContexts.AsNoTracking()
                .AnyAsync(x => x.RaceId == id, token);
            if (existing) return Results.Ok(new CreateRaceFromScheduleResponse(id));
            try
            {
                await commands.PublishAsync(new CreateRaceCommand(new RaceId(id), schedule.RaceDate,
                    course, schedule.RaceNumber, schedule.RaceName), token);
            }
            catch (InvalidOperationException exception)
                when (exception.Message == "Race is already created.")
            {
                return Results.Ok(new CreateRaceFromScheduleResponse(id));
            }
            return Results.Created($"/api/v2/admin/races/{Uri.EscapeDataString(id)}", new CreateRaceFromScheduleResponse(id));
        }).AddEndpointFilter<RaceWriteEndpointFilter>()
            .WithName("CreateRaceFromSchedule")
            .WithTags("Race API")
            .Produces<CreateRaceFromScheduleResponse>(StatusCodes.Status200OK)
            .Produces<CreateRaceFromScheduleResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);
    }
}
