using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.Core;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api;

public static class EndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { Status = "ok" }))
            .WithName("Health")
            .WithSummary("Health check")
            .WithOpenApi();

        var writeGroup = app.MapGroup("/api")
            .AddEndpointFilter<ApiKeyEndpointFilter>();

        writeGroup.MapPost("/races/{raceId}",
            [SwaggerOperation(Summary = "Create race", Description = "Creates a race aggregate in Draft state")]
            async (string raceId, CreateRaceRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CreateRaceCommand(
                    new RaceId(raceId),
                    request.RaceDate,
                    request.RacecourseCode,
                    request.RaceNumber,
                    request.RaceName);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/races/{raceId}", new { RaceId = raceId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CreateRace")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/card/publish",
            [SwaggerOperation(Summary = "Publish race card", Description = "Moves lifecycle from Draft to CardPublished")]
            async (string raceId, PublishRaceCardRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new PublishRaceCardCommand(new RaceId(raceId), request.EntryCount);
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("PublishRaceCard")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/result",
            [SwaggerOperation(Summary = "Declare race result", Description = "Declares result and moves lifecycle to ResultDeclared")]
            async (string raceId, DeclareRaceResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new DeclareRaceResultCommand(
                    new RaceId(raceId),
                    request.WinningHorseName,
                    request.DeclaredAt ?? DateTimeOffset.UtcNow);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("DeclareRaceResult")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}",
            [SwaggerOperation(Summary = "Create prediction ticket", Description = "Creates one prediction ticket for a race")]
            async (string predictionTicketId, CreatePredictionTicketRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CreatePredictionTicketCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.RaceId,
                    request.PredictorType,
                    request.PredictorId,
                    request.ConfidenceScore,
                    request.SummaryComment);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/predictions/{predictionTicketId}", new { PredictionTicketId = predictionTicketId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CreatePredictionTicket")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}/marks",
            [SwaggerOperation(Summary = "Add prediction mark", Description = "Appends a mark record to prediction ticket")]
            async (string predictionTicketId, AddPredictionMarkRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new AddPredictionMarkCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.EntryId,
                    request.MarkCode,
                    request.PredictedRank,
                    request.Score,
                    request.Comment);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("AddPredictionMark")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        app.MapGet("/api/races/{raceId}",
            [SwaggerOperation(Summary = "Get race", Description = "Loads race aggregate and returns current snapshot view")]
            async (string raceId, IAggregateStore aggregateStore, CancellationToken cancellationToken) =>
            {
                var aggregate = await TryLoadAggregate<RaceAggregate, RaceId>(
                    aggregateStore,
                    new RaceId(raceId),
                    cancellationToken)
                    .ConfigureAwait(false);

                if (aggregate is null)
                {
                    return Results.NotFound();
                }

                var details = aggregate.GetDetails();
                var response = new RaceResponse(
                    details.RaceId,
                    details.RaceDate,
                    details.RacecourseCode,
                    details.RaceNumber,
                    details.RaceName,
                    details.Status,
                    details.MeetingNumber,
                    details.DayNumber,
                    details.GradeCode,
                    details.SurfaceCode,
                    details.DistanceMeters,
                    details.DirectionCode,
                    details.EntryCount,
                    details.WinningHorseName,
                    details.ResultDeclaredAt);

                return Results.Ok(response);
            })
            .WithName("GetRace")
            .Produces<RaceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/predictions/{predictionTicketId}",
            [SwaggerOperation(Summary = "Get prediction ticket", Description = "Loads prediction aggregate and returns marks")]
            async (string predictionTicketId, IAggregateStore aggregateStore, CancellationToken cancellationToken) =>
            {
                var aggregate = await TryLoadAggregate<PredictionTicketAggregate, PredictionTicketId>(
                    aggregateStore,
                    new PredictionTicketId(predictionTicketId),
                    cancellationToken)
                    .ConfigureAwait(false);

                if (aggregate is null)
                {
                    return Results.NotFound();
                }

                var details = aggregate.GetDetails();
                var response = new PredictionTicketResponse(
                    details.PredictionTicketId,
                    details.RaceId,
                    details.PredictorType,
                    details.PredictorId,
                    details.ConfidenceScore,
                    details.SummaryComment,
                    details.PredictedAt,
                    details.Marks
                        .Select(x => new PredictionMarkResponse(x.EntryId, x.MarkCode, x.PredictedRank, x.Score, x.Comment))
                        .ToList());

                return Results.Ok(response);
            })
            .WithName("GetPredictionTicket")
            .Produces<PredictionTicketResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        return app;
    }

    private static async Task<TAggregate?> TryLoadAggregate<TAggregate, TIdentity>(
        IAggregateStore aggregateStore,
        TIdentity aggregateId,
        CancellationToken cancellationToken)
        where TAggregate : class, IAggregateRoot<TIdentity>
        where TIdentity : IIdentity
    {
        try
        {
            return await aggregateStore.LoadAsync<TAggregate, TIdentity>(aggregateId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
