using EventFlow;
using EventFlow.Commands;
using EventFlow.Queries;
using EventFlow.ReadStores.InMemory;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
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
            [SwaggerOperation(Summary = "Get race", Description = "Returns race read model with current status and result information")]
            async (string raceId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<RaceResultViewReadModel>(raceId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                    return Results.NotFound();

                var response = new RaceResponse(
                    readModel.RaceId,
                    readModel.RaceDate,
                    readModel.RacecourseCode,
                    readModel.RaceNumber,
                    readModel.RaceName,
                    readModel.Status,
                    null, null,
                    null, null, null, null,
                    readModel.EntryCount,
                    readModel.WinningHorseName,
                    readModel.ResultDeclaredAt);

                return Results.Ok(response);
            })
            .WithName("GetRace")
            .Produces<RaceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/races/{raceId}/context",
            [SwaggerOperation(Summary = "Get race prediction context", Description = "Returns prediction context read model including entries, weather and track conditions")]
            async (string raceId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                    return Results.NotFound();

                return Results.Ok(readModel);
            })
            .WithName("GetRacePredictionContext")
            .Produces<RacePredictionContextReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/races/{raceId}/comparison",
            [SwaggerOperation(Summary = "Get prediction comparison view", Description = "Returns prediction vs result comparison for a race")]
            async (string raceId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<PredictionComparisonViewReadModel>(raceId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                    return Results.NotFound();

                return Results.Ok(readModel);
            })
            .WithName("GetPredictionComparison")
            .Produces<PredictionComparisonViewReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/predictions/{predictionTicketId}",
            [SwaggerOperation(Summary = "Get prediction ticket", Description = "Returns prediction ticket read model")]
            async (string predictionTicketId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<PredictionTicketReadModel>(predictionTicketId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.PredictionTicketId))
                    return Results.NotFound();

                var response = new PredictionTicketResponse(
                    readModel.PredictionTicketId,
                    readModel.RaceId,
                    readModel.PredictorType,
                    readModel.PredictorId,
                    readModel.ConfidenceScore,
                    readModel.SummaryComment,
                    readModel.PredictedAt,
                    readModel.Marks
                        .Select(x => new PredictionMarkResponse(x.EntryId, x.MarkCode, x.PredictedRank, x.Score, x.Comment))
                        .ToList());

                return Results.Ok(response);
            })
            .WithName("GetPredictionTicket")
            .Produces<PredictionTicketResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/horses/{horseId}",
            [SwaggerOperation(Summary = "Get horse profile", Description = "Returns horse profile read model")]
            async (string horseId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<HorseReadModel>(horseId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.HorseId))
                    return Results.NotFound();

                var response = new HorseProfileResponse(
                    readModel.HorseId,
                    readModel.RegisteredName,
                    readModel.NormalizedName,
                    readModel.SexCode,
                    readModel.BirthDate,
                    readModel.Aliases
                        .Select(a => new AliasResponse(a.AliasType, a.AliasValue, a.SourceName, a.IsPrimary))
                        .ToList());

                return Results.Ok(response);
            })
            .WithName("GetHorseProfile")
            .Produces<HorseProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/horses/{horseId}/weight-history",
            [SwaggerOperation(Summary = "Get horse weight history", Description = "Returns horse body weight history across races")]
            async (string horseId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<HorseWeightHistoryReadModel>(horseId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.HorseId))
                    return Results.NotFound();

                var response = new HorseWeightHistoryResponse(
                    readModel.HorseId,
                    readModel.WeightHistory
                        .OrderByDescending(w => w.RecordedAt)
                        .Select(w => new HorseWeightEntryResponse(w.RaceId, w.EntryId, w.RecordedAt, w.DeclaredWeight, w.DeclaredWeightDiff))
                        .ToList());

                return Results.Ok(response);
            })
            .WithName("GetHorseWeightHistory")
            .Produces<HorseWeightHistoryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/jockeys/{jockeyId}",
            [SwaggerOperation(Summary = "Get jockey profile", Description = "Returns jockey profile read model")]
            async (string jockeyId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<JockeyReadModel>(jockeyId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.JockeyId))
                    return Results.NotFound();

                var response = new JockeyProfileResponse(
                    readModel.JockeyId,
                    readModel.DisplayName,
                    readModel.NormalizedName,
                    readModel.AffiliationCode,
                    readModel.Aliases
                        .Select(a => new AliasResponse(a.AliasType, a.AliasValue, a.SourceName, a.IsPrimary))
                        .ToList());

                return Results.Ok(response);
            })
            .WithName("GetJockeyProfile")
            .Produces<JockeyProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/trainers/{trainerId}",
            [SwaggerOperation(Summary = "Get trainer profile", Description = "Returns trainer profile read model")]
            async (string trainerId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<TrainerReadModel>(trainerId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.TrainerId))
                    return Results.NotFound();

                var response = new TrainerProfileResponse(
                    readModel.TrainerId,
                    readModel.DisplayName,
                    readModel.NormalizedName,
                    readModel.AffiliationCode,
                    readModel.Aliases
                        .Select(a => new AliasResponse(a.AliasType, a.AliasValue, a.SourceName, a.IsPrimary))
                        .ToList());

                return Results.Ok(response);
            })
            .WithName("GetTrainerProfile")
            .Produces<TrainerProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        return app;
    }
}
