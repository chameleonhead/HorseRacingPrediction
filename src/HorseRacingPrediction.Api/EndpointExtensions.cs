using EventFlow;
using EventFlow.Commands;
using EventFlow.Queries;
using EventFlow.ReadStores.InMemory;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
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

        writeGroup.MapPost("/horses/{horseId}",
            [SwaggerOperation(Summary = "Register horse", Description = "Registers a new horse")]
            async (string horseId, RegisterHorseRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new RegisterHorseCommand(
                    new HorseId(horseId),
                    request.RegisteredName,
                    request.NormalizedName,
                    request.SexCode,
                    request.BirthDate);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/horses/{horseId}", new { HorseId = horseId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RegisterHorse")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPut("/horses/{horseId}",
            [SwaggerOperation(Summary = "Update horse profile", Description = "Updates profile information of an existing horse")]
            async (string horseId, UpdateHorseProfileRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new UpdateHorseProfileCommand(
                    new HorseId(horseId),
                    request.RegisteredName,
                    request.NormalizedName,
                    request.SexCode,
                    request.BirthDate);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("UpdateHorseProfile")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/horses/{horseId}/aliases",
            [SwaggerOperation(Summary = "Merge horse alias", Description = "Adds or updates an alias for a horse from an external data source")]
            async (string horseId, MergeAliasRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new MergeHorseAliasCommand(
                    new HorseId(horseId),
                    request.AliasType,
                    request.AliasValue,
                    request.SourceName,
                    request.IsPrimary);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("MergeHorseAlias")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/jockeys/{jockeyId}",
            [SwaggerOperation(Summary = "Register jockey", Description = "Registers a new jockey")]
            async (string jockeyId, RegisterJockeyRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new RegisterJockeyCommand(
                    new JockeyId(jockeyId),
                    request.DisplayName,
                    request.NormalizedName,
                    request.AffiliationCode);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/jockeys/{jockeyId}", new { JockeyId = jockeyId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RegisterJockey")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPut("/jockeys/{jockeyId}",
            [SwaggerOperation(Summary = "Update jockey profile", Description = "Updates profile information of an existing jockey")]
            async (string jockeyId, UpdateJockeyProfileRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new UpdateJockeyProfileCommand(
                    new JockeyId(jockeyId),
                    request.DisplayName,
                    request.NormalizedName,
                    request.AffiliationCode);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("UpdateJockeyProfile")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/jockeys/{jockeyId}/aliases",
            [SwaggerOperation(Summary = "Merge jockey alias", Description = "Adds or updates an alias for a jockey from an external data source")]
            async (string jockeyId, MergeAliasRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new MergeJockeyAliasCommand(
                    new JockeyId(jockeyId),
                    request.AliasType,
                    request.AliasValue,
                    request.SourceName,
                    request.IsPrimary);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("MergeJockeyAlias")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/trainers/{trainerId}",
            [SwaggerOperation(Summary = "Register trainer", Description = "Registers a new trainer")]
            async (string trainerId, RegisterTrainerRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new RegisterTrainerCommand(
                    new TrainerId(trainerId),
                    request.DisplayName,
                    request.NormalizedName,
                    request.AffiliationCode);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/trainers/{trainerId}", new { TrainerId = trainerId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RegisterTrainer")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPut("/trainers/{trainerId}",
            [SwaggerOperation(Summary = "Update trainer profile", Description = "Updates profile information of an existing trainer")]
            async (string trainerId, UpdateTrainerProfileRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new UpdateTrainerProfileCommand(
                    new TrainerId(trainerId),
                    request.DisplayName,
                    request.NormalizedName,
                    request.AffiliationCode);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("UpdateTrainerProfile")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/trainers/{trainerId}/aliases",
            [SwaggerOperation(Summary = "Merge trainer alias", Description = "Adds or updates an alias for a trainer from an external data source")]
            async (string trainerId, MergeAliasRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new MergeTrainerAliasCommand(
                    new TrainerId(trainerId),
                    request.AliasType,
                    request.AliasValue,
                    request.SourceName,
                    request.IsPrimary);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("MergeTrainerAlias")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

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

        writeGroup.MapPost("/races/{raceId}/entries/{entryId}",
            [SwaggerOperation(Summary = "Register entry", Description = "Registers a horse entry for a race after card publication")]
            async (string raceId, string entryId, RegisterEntryRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new RegisterEntryCommand(
                    new RaceId(raceId),
                    entryId,
                    request.HorseId,
                    request.HorseNumber,
                    request.JockeyId,
                    request.TrainerId,
                    request.GateNumber,
                    request.AssignedWeight,
                    request.SexCode,
                    request.Age,
                    request.DeclaredWeight,
                    request.DeclaredWeightDiff);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/races/{raceId}/entries/{entryId}", new { RaceId = raceId, EntryId = entryId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RegisterEntry")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/weather",
            [SwaggerOperation(Summary = "Record weather observation", Description = "Records a weather observation for a race")]
            async (string raceId, RecordWeatherObservationRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new RecordWeatherObservationCommand(
                    new RaceId(raceId),
                    request.ObservationTime,
                    request.WeatherCode,
                    request.WeatherText,
                    request.TemperatureCelsius,
                    request.HumidityPercent,
                    request.WindDirectionCode,
                    request.WindSpeedMeterPerSecond);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RecordWeatherObservation")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/track-condition",
            [SwaggerOperation(Summary = "Record track condition", Description = "Records a track condition observation for a race")]
            async (string raceId, RecordTrackConditionRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new RecordTrackConditionObservationCommand(
                    new RaceId(raceId),
                    request.ObservationTime,
                    request.TurfConditionCode,
                    request.DirtConditionCode,
                    request.GoingDescriptionText);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RecordTrackCondition")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/open-pre-race",
            [SwaggerOperation(Summary = "Open pre-race", Description = "Moves race lifecycle from CardPublished to PreRaceOpen")]
            async (string raceId, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new OpenPreRaceCommand(new RaceId(raceId));
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("OpenPreRace")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/start",
            [SwaggerOperation(Summary = "Start race", Description = "Moves race lifecycle from PreRaceOpen to InProgress")]
            async (string raceId, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new StartRaceCommand(new RaceId(raceId));
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("StartRace")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/entries/{entryId}/result",
            [SwaggerOperation(Summary = "Declare entry result", Description = "Declares finish result for a specific entry after race result is declared")]
            async (string raceId, string entryId, DeclareEntryResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new DeclareEntryResultCommand(
                    new RaceId(raceId),
                    entryId,
                    request.FinishPosition,
                    request.OfficialTime,
                    request.MarginText,
                    request.LastThreeFurlongTime,
                    request.AbnormalResultCode,
                    request.PrizeMoney);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("DeclareEntryResult")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/payout",
            [SwaggerOperation(Summary = "Declare payout result", Description = "Declares payout information for win/place/quinella/exacta/trifecta bets")]
            async (string raceId, DeclarePayoutResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                static IReadOnlyList<PayoutEntry>? ToPayoutEntries(IReadOnlyList<PayoutEntryDto>? dtos) =>
                    dtos?.Select(d => new PayoutEntry(d.Combination, d.Amount)).ToList();

                var command = new DeclarePayoutResultCommand(
                    new RaceId(raceId),
                    request.DeclaredAt,
                    ToPayoutEntries(request.WinPayouts),
                    ToPayoutEntries(request.PlacePayouts),
                    ToPayoutEntries(request.QuinellaPayouts),
                    ToPayoutEntries(request.ExactaPayouts),
                    ToPayoutEntries(request.TrifectaPayouts));

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("DeclarePayoutResult")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/close",
            [SwaggerOperation(Summary = "Close race lifecycle", Description = "Closes the race lifecycle from ResultDeclared or PayoutDeclared state")]
            async (string raceId, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CloseRaceLifecycleCommand(new RaceId(raceId));
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CloseRaceLifecycle")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPatch("/races/{raceId}",
            [SwaggerOperation(Summary = "Correct race data", Description = "Corrects race metadata such as name, racecourse, grade, surface or distance")]
            async (string raceId, CorrectRaceDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CorrectRaceDataCommand(
                    new RaceId(raceId),
                    request.RaceName,
                    request.RacecourseCode,
                    request.RaceNumber,
                    request.GradeCode,
                    request.SurfaceCode,
                    request.DistanceMeters,
                    request.DirectionCode,
                    request.Reason);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CorrectRaceData")
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

        writeGroup.MapPost("/memos/{memoId}",
            [SwaggerOperation(Summary = "Create memo", Description = "Creates a memo that can be attached to any combination of subjects (horse, trainer, jockey, race)")]
            async (string memoId, CreateMemoRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                if (request.Subjects is null || request.Subjects.Count == 0)
                    return Results.BadRequest(new[] { "At least one subject is required." });

                var subjects = request.Subjects
                    .Select(s => new MemoSubject(Enum.Parse<MemoSubjectType>(s.SubjectType, ignoreCase: true), s.SubjectId))
                    .ToList();

                var links = (request.Links ?? Array.Empty<MemoLinkDto>())
                    .Select(l => new MemoLink(l.LinkId, Enum.Parse<MemoLinkType>(l.LinkType, ignoreCase: true), l.Title, l.Url, l.StorageKey))
                    .ToList();

                var command = new CreateMemoCommand(
                    new MemoId(memoId),
                    request.AuthorId,
                    request.MemoType,
                    request.Content,
                    request.CreatedAt,
                    subjects,
                    links);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/memos/{memoId}", new { MemoId = memoId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CreateMemo")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPut("/memos/{memoId}",
            [SwaggerOperation(Summary = "Update memo", Description = "Updates content or links of an existing memo")]
            async (string memoId, UpdateMemoRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var links = request.Links?.Select(l =>
                    new MemoLink(l.LinkId, Enum.Parse<MemoLinkType>(l.LinkType, ignoreCase: true), l.Title, l.Url, l.StorageKey))
                    .ToList();

                var command = new UpdateMemoCommand(new MemoId(memoId), request.MemoType, request.Content, links);
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("UpdateMemo")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapDelete("/memos/{memoId}",
            [SwaggerOperation(Summary = "Delete memo", Description = "Deletes a memo")]
            async (string memoId, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new DeleteMemoCommand(new MemoId(memoId));
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("DeleteMemo")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPut("/memos/{memoId}/subjects",
            [SwaggerOperation(Summary = "Change memo subjects", Description = "Replaces the full list of subjects for a memo")]
            async (string memoId, ChangeMemoSubjectsRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                if (request.Subjects is null || request.Subjects.Count == 0)
                    return Results.BadRequest(new[] { "At least one subject is required." });

                var subjects = request.Subjects
                    .Select(s => new MemoSubject(Enum.Parse<MemoSubjectType>(s.SubjectType, ignoreCase: true), s.SubjectId))
                    .ToList();

                var command = new ChangeMemoSubjectsCommand(new MemoId(memoId), subjects);
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("ChangeMemoSubjects")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        app.MapGet("/api/memos/by-subject/{subjectType}/{subjectId}",
            [SwaggerOperation(Summary = "Get memos by subject", Description = "Returns all memos for a given subject (e.g. Horse, Trainer, Jockey, Race). Use subjectType=Horse and subjectId=<horseId>.")]
            async (string subjectType, string subjectId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                if (!Enum.TryParse<MemoSubjectType>(subjectType, ignoreCase: true, out var parsedType))
                    return Results.BadRequest(new[] { $"Unknown subjectType '{subjectType}'." });

                var key = MemoBySubjectLocator.MakeKey(parsedType, subjectId);
                var query = new ReadModelByIdQuery<MemoBySubjectReadModel>(key);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.SubjectKey))
                    return Results.NotFound();

                var response = readModel.Memos.Select(m => new MemoResponse(
                    m.MemoId, m.AuthorId, m.MemoType, m.Content, m.CreatedAt,
                    m.Subjects.Select(s => new MemoSubjectDto(s.SubjectType, s.SubjectId)).ToList(),
                    m.Links.Select(l => new MemoLinkDto(l.LinkId, l.LinkType, l.Title, l.Url, l.StorageKey)).ToList()))
                    .ToList();

                return Results.Ok(response);
            })
            .WithName("GetMemosBySubject")
            .Produces<IReadOnlyList<MemoResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        return app;
    }
}
