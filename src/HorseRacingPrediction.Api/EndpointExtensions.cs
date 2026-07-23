using EventFlow;
using EventFlow.Commands;
using EventFlow.EntityFramework;
using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Queries.ReadModels;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using HorseRacingPrediction.MachineLearning.Prediction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api;

public static class EndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { Status = "ok" }))
            .WithName("Health")
            .WithTags("Health")
            .WithSummary("Health check")
            .WithOpenApi();

        var writeGroup = app.MapGroup("/api")
            .AddEndpointFilter<ApiKeyEndpointFilter>();

        writeGroup.MapPost("/horses",
            [SwaggerOperation(Summary = "Register horse", Description = "Registers a new horse")]
        async (RegisterHorseRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
                {
                    var horseId = string.IsNullOrWhiteSpace(request.HorseId) ? HorseId.New : new HorseId(request.HorseId);
                    var command = new RegisterHorseCommand(
                        horseId,
                        request.RegisteredName,
                        request.NormalizedName,
                        request.SexCode,
                        request.BirthDate,
                        request.OwnerName);

                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Created($"/api/horses/{horseId.Value}", new { HorseId = horseId.Value })
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Horse is already registered.", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("RegisterHorse")
            .WithTags("Horse API")
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
                    request.BirthDate,
                    request.OwnerName);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("UpdateHorseProfile")
            .WithTags("Horse API")
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
            .WithTags("Horse API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPatch("/horses/{horseId}",
            [SwaggerOperation(Summary = "Correct horse data", Description = "Corrects horse master data with an optional audit reason")]
        async (string horseId, CorrectHorseDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CorrectHorseDataCommand(
                    new HorseId(horseId),
                    request.RegisteredName,
                    request.NormalizedName,
                    request.SexCode,
                    request.BirthDate,
                    request.Reason);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CorrectHorseData")
            .WithTags("Horse API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/jockeys",
            [SwaggerOperation(Summary = "Register jockey", Description = "Registers a new jockey")]
        async (RegisterJockeyRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
                {
                    var jockeyId = string.IsNullOrWhiteSpace(request.JockeyId) ? JockeyId.New : new JockeyId(request.JockeyId);
                    var command = new RegisterJockeyCommand(
                        jockeyId,
                        request.DisplayName,
                        request.NormalizedName,
                        request.AffiliationCode);

                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Created($"/api/jockeys/{jockeyId.Value}", new { JockeyId = jockeyId.Value })
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Jockey is already registered.", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("RegisterJockey")
            .WithTags("Jockey API")
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
            .WithTags("Jockey API")
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
            .WithTags("Jockey API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPatch("/jockeys/{jockeyId}",
            [SwaggerOperation(Summary = "Correct jockey data", Description = "Corrects jockey master data with an optional audit reason")]
        async (string jockeyId, CorrectJockeyDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CorrectJockeyDataCommand(
                    new JockeyId(jockeyId),
                    request.DisplayName,
                    request.NormalizedName,
                    request.AffiliationCode,
                    request.Reason);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CorrectJockeyData")
            .WithTags("Jockey API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/trainers",
            [SwaggerOperation(Summary = "Register trainer", Description = "Registers a new trainer")]
        async (RegisterTrainerRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
                {
                    var trainerId = string.IsNullOrWhiteSpace(request.TrainerId) ? TrainerId.New : new TrainerId(request.TrainerId);
                    var command = new RegisterTrainerCommand(
                        trainerId,
                        request.DisplayName,
                        request.NormalizedName,
                        request.AffiliationCode);

                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Created($"/api/trainers/{trainerId.Value}", new { TrainerId = trainerId.Value })
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Trainer is already registered.", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("RegisterTrainer")
            .WithTags("Trainer API")
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
            .WithTags("Trainer API")
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
            .WithTags("Trainer API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPatch("/trainers/{trainerId}",
            [SwaggerOperation(Summary = "Correct trainer data", Description = "Corrects trainer master data with an optional audit reason")]
        async (string trainerId, CorrectTrainerDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CorrectTrainerDataCommand(
                    new TrainerId(trainerId),
                    request.DisplayName,
                    request.NormalizedName,
                    request.AffiliationCode,
                    request.Reason);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CorrectTrainerData")
            .WithTags("Trainer API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races",
            [SwaggerOperation(Summary = "Create race", Description = "Creates a race aggregate in Draft state")]
        async (CreateRaceRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
                {
                    var raceId = string.IsNullOrWhiteSpace(request.RaceId) ? RaceId.New : new RaceId(request.RaceId);
                    var command = new CreateRaceCommand(
                        raceId,
                        request.RaceDate,
                        request.RacecourseCode,
                        request.RaceNumber,
                        request.RaceName);

                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Created($"/api/races/{raceId.Value}", new { RaceId = raceId.Value })
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Race is already created.", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("CreateRace")
            .WithTags("Race API")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        app.MapGet("/api/races",
            [SwaggerOperation(Summary = "Search races", Description = "Returns paged race summaries filtered by date, course, status, race name and result information")]
        async ([AsParameters] SearchRacesRequest request,
                IDbContextProvider<EventStoreDbContext> dbContextProvider,
                CancellationToken cancellationToken) =>
            {
                var page = request.Page ?? 1;
                var pageSize = request.PageSize ?? 20;
                var sortBy = request.SortBy ?? "raceDate";
                var sortDescending = request.SortDescending ?? true;

                var pagingError = ValidatePaging(page, pageSize);
                if (pagingError is not null)
                    return Results.BadRequest(new[] { pagingError });

                using var dbContext = dbContextProvider.CreateContext();
                var allRaces = await dbContext.Set<RaceSummaryReadModel>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IEnumerable<RaceSummaryReadModel> filtered = allRaces;

                if (!string.IsNullOrWhiteSpace(request.RaceId))
                    filtered = filtered.Where(x => string.Equals(x.RaceId, request.RaceId, StringComparison.OrdinalIgnoreCase));

                if (request.RaceDateFrom.HasValue)
                    filtered = filtered.Where(x => x.RaceDate.HasValue && x.RaceDate.Value >= request.RaceDateFrom.Value);

                if (request.RaceDateTo.HasValue)
                    filtered = filtered.Where(x => x.RaceDate.HasValue && x.RaceDate.Value <= request.RaceDateTo.Value);

                if (!string.IsNullOrWhiteSpace(request.RacecourseCode))
                    filtered = filtered.Where(x => string.Equals(x.RacecourseCode, request.RacecourseCode, StringComparison.OrdinalIgnoreCase));

                if (request.RaceNumber.HasValue)
                    filtered = filtered.Where(x => x.RaceNumber == request.RaceNumber.Value);

                if (!string.IsNullOrWhiteSpace(request.RaceName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.RaceName, request.RaceName));

                if (request.Status.HasValue)
                    filtered = filtered.Where(x => x.Status == (HorseRacingPrediction.Domain.Races.RaceStatus)request.Status.Value);

                if (!string.IsNullOrWhiteSpace(request.WinningHorseName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.WinningHorseName, request.WinningHorseName));

                filtered = sortBy.ToLowerInvariant() switch
                {
                    "racedate" => sortDescending
                        ? filtered.OrderByDescending(x => x.RaceDate).ThenByDescending(x => x.RaceNumber)
                        : filtered.OrderBy(x => x.RaceDate).ThenBy(x => x.RaceNumber),
                    "racenumber" => sortDescending
                        ? filtered.OrderByDescending(x => x.RaceNumber).ThenByDescending(x => x.RaceDate)
                        : filtered.OrderBy(x => x.RaceNumber).ThenBy(x => x.RaceDate),
                    "racename" => sortDescending
                        ? filtered.OrderByDescending(x => x.RaceName)
                        : filtered.OrderBy(x => x.RaceName),
                    "status" => sortDescending
                        ? filtered.OrderByDescending(x => x.Status)
                        : filtered.OrderBy(x => x.Status),
                    "resultdeclaredat" => sortDescending
                        ? filtered.OrderByDescending(x => x.ResultDeclaredAt).ThenByDescending(x => x.RaceDate)
                        : filtered.OrderBy(x => x.ResultDeclaredAt).ThenBy(x => x.RaceDate),
                    _ => null!
                };

                if (filtered is null)
                {
                    return Results.BadRequest(new[]
                    {
                        "SortBy must be one of: raceDate, raceNumber, raceName, status, resultDeclaredAt."
                    });
                }

                var totalCount = filtered.Count();
                var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
                var items = filtered
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new RaceSummaryResponse(
                        x.RaceId,
                        x.RaceDate,
                        x.RacecourseCode,
                        x.RaceNumber,
                        x.RaceName,
                        (ApiContracts.RaceStatus)(int)x.Status,
                        x.EntryCount,
                        x.WinningHorseName,
                        x.ResultDeclaredAt))
                    .ToList();

                return Results.Ok(new PagedResponse<RaceSummaryResponse>(
                    items,
                    page,
                    pageSize,
                    totalCount,
                    totalPages));
            })
            .WithName("SearchRaces")
            .WithTags("Race API")
            .Produces<PagedResponse<RaceSummaryResponse>>(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/card/publish",
            [SwaggerOperation(Summary = "Publish race card", Description = "Moves lifecycle from Draft to CardPublished")]
        async (string raceId, PublishRaceCardRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
                {
                    var command = new PublishRaceCardCommand(new RaceId(raceId), request.EntryCount);
                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Ok()
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Race card can only be published from Draft state.", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("PublishRaceCard")
            .WithTags("Race API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/result",
            [SwaggerOperation(Summary = "Declare race result", Description = "Declares result and moves lifecycle to ResultDeclared")]
        async (string raceId, DeclareRaceResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
                {
                    var command = new DeclareRaceResultCommand(
                        new RaceId(raceId),
                        request.WinningHorseName,
                        request.DeclaredAt ?? DateTimeOffset.UtcNow);

                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Ok()
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("DeclareRaceResult")
            .WithTags("Race API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/entries",
            [SwaggerOperation(Summary = "Register entry", Description = "Registers a horse entry for a race after card publication")]
        async (string raceId, RegisterEntryRequest request, ICommandBus commandBus, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
            {
                await EnsureRelatedSubjectsAsync(request, commandBus, dbContextProvider, cancellationToken).ConfigureAwait(false);

                var entryId = string.IsNullOrWhiteSpace(request.EntryId) ? $"entry-{Guid.NewGuid()}" : request.EntryId;
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
                    request.DeclaredWeightDiff,
                    request.RunningStyleCode);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/races/{raceId}/entries/{entryId}", new { RaceId = raceId, EntryId = entryId })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RegisterEntry")
            .WithTags("Race API")
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
            .WithTags("Race API")
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
            .WithTags("Race API")
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
            .WithTags("Race API")
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
            .WithTags("Race API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/entries/{entryId}/result",
            [SwaggerOperation(Summary = "Declare entry result", Description = "Declares finish result for a specific entry after race result is declared")]
        async (string raceId, string entryId, DeclareEntryResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
                {
                    var command = new DeclareEntryResultCommand(
                        new RaceId(raceId),
                        entryId,
                        request.FinishPosition,
                        request.OfficialTime,
                        request.MarginText,
                        request.LastThreeFurlongTime,
                        request.AbnormalResultCode,
                        request.PrizeMoney,
                        request.CornerPositions);

                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Ok()
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("DeclareEntryResult")
            .WithTags("Race API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/races/{raceId}/payout",
            [SwaggerOperation(Summary = "Declare payout result", Description = "Declares payout information for win/place/quinella/exacta/trifecta bets")]
        async (string raceId, DeclarePayoutResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                try
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
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("DeclarePayoutResult")
            .WithTags("Race API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
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
            .WithTags("Race API")
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
            .WithTags("Race API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions",
            [SwaggerOperation(Summary = "Create prediction ticket", Description = "Creates one prediction ticket for a race")]
        async (CreatePredictionTicketRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var predictionTicketId = string.IsNullOrWhiteSpace(request.PredictionTicketId)
                    ? PredictionTicketId.New : new PredictionTicketId(request.PredictionTicketId);
                var command = new CreatePredictionTicketCommand(
                    predictionTicketId,
                    request.RaceId,
                    request.PredictorType,
                    request.PredictorId,
                    request.ConfidenceScore,
                    request.SummaryComment);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/predictions/{predictionTicketId.Value}", new { PredictionTicketId = predictionTicketId.Value })
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CreatePredictionTicket")
            .WithTags("Prediction API")
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
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}/betting-suggestions",
            [SwaggerOperation(Summary = "Add betting suggestion", Description = "Appends a betting suggestion to prediction ticket")]
        async (string predictionTicketId, AddBettingSuggestionRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new AddBettingSuggestionCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.BetTypeCode,
                    request.SelectionExpression,
                    request.StakeAmount,
                    request.ExpectedValue);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("AddBettingSuggestion")
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}/rationales",
            [SwaggerOperation(Summary = "Add prediction rationale", Description = "Appends a rationale entry to prediction ticket")]
        async (string predictionTicketId, AddPredictionRationaleRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new AddPredictionRationaleCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.SubjectType,
                    request.SubjectId,
                    request.SignalType,
                    request.SignalValue,
                    request.ExplanationText);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("AddPredictionRationale")
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}/finalize",
            [SwaggerOperation(Summary = "Finalize prediction ticket", Description = "Moves prediction ticket from Draft to Finalized")]
        async (string predictionTicketId, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new FinalizePredictionTicketCommand(new PredictionTicketId(predictionTicketId));
                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("FinalizePredictionTicket")
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}/withdraw",
            [SwaggerOperation(Summary = "Withdraw prediction ticket", Description = "Withdraws a prediction ticket with an optional reason")]
        async (string predictionTicketId, WithdrawPredictionTicketRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new WithdrawPredictionTicketCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.Reason);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("WithdrawPredictionTicket")
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPatch("/predictions/{predictionTicketId}",
            [SwaggerOperation(Summary = "Correct prediction metadata", Description = "Corrects confidence score or summary comment of a prediction ticket")]
        async (string predictionTicketId, CorrectPredictionMetadataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new CorrectPredictionMetadataCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.ConfidenceScore,
                    request.SummaryComment,
                    request.Reason);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("CorrectPredictionMetadata")
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}/evaluate",
            [SwaggerOperation(Summary = "Evaluate prediction ticket", Description = "Records evaluation result by comparing prediction against actual race result")]
        async (string predictionTicketId, EvaluatePredictionTicketRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new EvaluatePredictionTicketCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.RaceId,
                    request.EvaluatedAt,
                    request.EvaluationRevision,
                    request.HitTypeCodes,
                    request.ScoreSummary,
                    request.ReturnAmount,
                    request.Roi);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("EvaluatePredictionTicket")
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        writeGroup.MapPost("/predictions/{predictionTicketId}/recalculate-evaluation",
            [SwaggerOperation(Summary = "Recalculate prediction evaluation", Description = "Recalculates evaluation of a prediction ticket with updated data")]
        async (string predictionTicketId, RecalculatePredictionEvaluationRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                var command = new RecalculatePredictionEvaluationCommand(
                    new PredictionTicketId(predictionTicketId),
                    request.RaceId,
                    request.EvaluatedAt,
                    request.EvaluationRevision,
                    request.HitTypeCodes,
                    request.ScoreSummary,
                    request.ReturnAmount,
                    request.Roi);

                var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok()
                    : Results.BadRequest(new[] { "Command execution failed." });
            })
            .WithName("RecalculatePredictionEvaluation")
            .WithTags("Prediction API")
            .Produces(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

        app.MapGet("/api/races/{raceId}",
            [SwaggerOperation(Summary = "Get race", Description = "Returns race read model with current status and result information")]
        async (string raceId, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
            {
                using var dbContext = dbContextProvider.CreateContext();
                var readModel = await dbContext.Set<AppReadModels.RacePredictionContextReadModel>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.RaceId == raceId, cancellationToken)
                    .ConfigureAwait(false);

                var resultReadModel = await dbContext.Set<RaceResultViewReadModel>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.RaceId == raceId, cancellationToken)
                    .ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                    return Results.NotFound();

                var entryHorseIdsByEntryId = readModel.Entries
                    .Where(x => !string.IsNullOrWhiteSpace(x.EntryId) && !string.IsNullOrWhiteSpace(x.HorseId))
                    .ToDictionary(x => x.EntryId, x => x.HorseId, StringComparer.Ordinal);

                var entryHorseNumbersByEntryId = readModel.Entries
                    .Where(x => !string.IsNullOrWhiteSpace(x.EntryId))
                    .ToDictionary(x => x.EntryId, x => x.HorseNumber, StringComparer.Ordinal);

                var entryGateNumbersByEntryId = readModel.Entries
                    .Where(x => !string.IsNullOrWhiteSpace(x.EntryId) && x.GateNumber.HasValue)
                    .ToDictionary(x => x.EntryId, x => x.GateNumber!.Value, StringComparer.Ordinal);

                var resultEntryGateNumbersByEntryId = resultReadModel?.EntryIndexes
                    .Where(x => !string.IsNullOrWhiteSpace(x.EntryId) && x.GateNumber.HasValue)
                    .ToDictionary(x => x.EntryId, x => x.GateNumber!.Value, StringComparer.Ordinal)
                    ?? new Dictionary<string, int>(StringComparer.Ordinal);

                var horseIds = readModel.Entries
                    .Select(x => x.HorseId)
                    .Concat(resultReadModel?.EntryResults.Select(x => ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)) ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var horseNamesById = horseIds.Count == 0
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : await dbContext.Set<AppReadModels.HorseReadModel>()
                        .AsNoTracking()
                        .Where(x => horseIds.Contains(x.HorseId))
                        .ToDictionaryAsync(x => x.HorseId, x => x.RegisteredName, StringComparer.Ordinal, cancellationToken)
                        .ConfigureAwait(false);

                var jockeyIds = readModel.Entries
                    .Select(x => x.JockeyId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var trainerIds = readModel.Entries
                    .Select(x => x.TrainerId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var jockeyNamesById = jockeyIds.Count == 0
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : await dbContext.Set<AppReadModels.JockeyReadModel>()
                        .AsNoTracking()
                        .Where(x => jockeyIds.Contains(x.JockeyId))
                        .ToDictionaryAsync(x => x.JockeyId, x => x.DisplayName, StringComparer.Ordinal, cancellationToken)
                        .ConfigureAwait(false);

                var trainerNamesById = trainerIds.Count == 0
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : await dbContext.Set<TrainerReadModel>()
                        .AsNoTracking()
                        .Where(x => trainerIds.Contains(x.TrainerId))
                        .ToDictionaryAsync(x => x.TrainerId, x => x.DisplayName, StringComparer.Ordinal, cancellationToken)
                        .ConfigureAwait(false);

                var entryResponses = readModel.Entries.Count > 0
                    ? readModel.Entries.Select(x => ToRaceEntryResponse(
                        x,
                        ResolveHorseName(horseNamesById, x.HorseId),
                        ResolveJockeyName(jockeyNamesById, x.JockeyId),
                        ResolveTrainerName(trainerNamesById, x.TrainerId),
                        ResolveGateNumber(entryGateNumbersByEntryId, resultEntryGateNumbersByEntryId, x.EntryId, x.HorseNumber))).ToList()
                    : resultReadModel?.EntryResults.Select(x => ToRaceEntryResponse(
                        x,
                        ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId),
                        ResolveHorseNumber(entryHorseNumbersByEntryId, x.EntryId, x.HorseNumber),
                        ResolveGateNumber(entryGateNumbersByEntryId, resultEntryGateNumbersByEntryId, x.EntryId, x.HorseNumber),
                        ResolveHorseName(horseNamesById, ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)))).ToList() ?? [];

                var winningHorseId = resultReadModel?.WinningHorseId;
                if (string.IsNullOrWhiteSpace(winningHorseId))
                {
                    var winnerEntry = resultReadModel?.EntryResults
                        .FirstOrDefault(x => x.FinishPosition == 1);
                    if (winnerEntry is not null)
                    {
                        winningHorseId = ResolveHorseId(entryHorseIdsByEntryId, winnerEntry.EntryId, winnerEntry.HorseId);
                    }
                }

                var winningHorseName = resultReadModel?.WinningHorseName;
                if (string.IsNullOrWhiteSpace(winningHorseName) && !string.IsNullOrWhiteSpace(winningHorseId))
                {
                    winningHorseName = ResolveHorseName(horseNamesById, winningHorseId);
                }

                var response = new RaceResponse(
                    readModel.RaceId,
                    readModel.RaceDate,
                    readModel.RacecourseCode,
                    readModel.RaceNumber,
                    readModel.RaceName,
                    (ApiContracts.RaceStatus)(int)readModel.Status,
                    null, null,
                    readModel.GradeCode,
                    readModel.SurfaceCode,
                    readModel.DistanceMeters,
                    readModel.DirectionCode,
                    resultReadModel?.EntryCount ?? readModel.Entries.Count,
                    entryResponses,
                    readModel.WeatherObservations.Select(ToRaceWeatherObservationResponse).ToList(),
                    readModel.TrackConditionObservations.Select(ToRaceTrackConditionResponse).ToList(),
                    BuildUnavailableOddsResponse(),
                    winningHorseName,
                    winningHorseId,
                    resultReadModel?.StewardReportText,
                    resultReadModel?.ResultDeclaredAt,
                    resultReadModel?.EntryResults.Select(x => ToRaceEntryResultResponse(
                        x,
                        ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId),
                        ResolveHorseNumber(entryHorseNumbersByEntryId, x.EntryId, x.HorseNumber),
                        ResolveHorseName(horseNamesById, ResolveHorseId(entryHorseIdsByEntryId, x.EntryId, x.HorseId)))).ToList() ?? [],
                    resultReadModel?.PayoutResult is null ? null : ToRacePayoutResultResponse(resultReadModel.PayoutResult));

                return Results.Ok(response);
            })
            .WithName("GetRace")
            .WithTags("Race API")
            .Produces<RaceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/races/{raceId}/context",
            [SwaggerOperation(Summary = "Get race prediction context", Description = "Returns prediction context read model including entries, weather and track conditions")]
        async (string raceId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<AppReadModels.RacePredictionContextReadModel>(raceId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                    return Results.NotFound();

                return Results.Ok(ToAgentRacePredictionContext(readModel));
            })
            .WithName("GetRacePredictionContext")
            .WithTags("Race API")
            .Produces<ApiContracts.RacePredictionContextReadModel>(StatusCodes.Status200OK)
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
            .WithTags("Race API")
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
            .WithTags("Prediction API")
            .Produces<PredictionTicketResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/predictions",
            [SwaggerOperation(Summary = "Search prediction tickets", Description = "Returns paged prediction ticket summaries filtered by race, predictor, ticket status, evaluation status and confidence score")]
        async ([AsParameters] SearchPredictionTicketsRequest request,
                IDbContextProvider<EventStoreDbContext> dbContextProvider,
                CancellationToken cancellationToken) =>
            {
                var page = request.Page ?? 1;
                var pageSize = request.PageSize ?? 20;
                var pagingError = ValidatePaging(page, pageSize);
                if (pagingError is not null)
                    return Results.BadRequest(new[] { pagingError });

                using var dbContext = dbContextProvider.CreateContext();
                var allTickets = await dbContext.Set<PredictionTicketReadModel>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IEnumerable<PredictionTicketReadModel> filtered = allTickets;

                if (!string.IsNullOrWhiteSpace(request.PredictionTicketId))
                    filtered = filtered.Where(x => string.Equals(x.PredictionTicketId, request.PredictionTicketId, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.RaceId))
                    filtered = filtered.Where(x => string.Equals(x.RaceId, request.RaceId, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.PredictorType))
                    filtered = filtered.Where(x => string.Equals(x.PredictorType, request.PredictorType, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.PredictorId))
                    filtered = filtered.Where(x => string.Equals(x.PredictorId, request.PredictorId, StringComparison.OrdinalIgnoreCase));

                if (request.TicketStatus.HasValue)
                    filtered = filtered.Where(x => x.TicketStatus == (HorseRacingPrediction.Domain.Predictions.TicketStatus)request.TicketStatus.Value);

                if (request.EvaluationStatus.HasValue)
                    filtered = filtered.Where(x => x.EvaluationStatus == (HorseRacingPrediction.Application.Queries.ReadModels.EvaluationStatus)request.EvaluationStatus.Value);

                if (request.PredictedAtFrom.HasValue)
                    filtered = filtered.Where(x => x.PredictedAt.HasValue && x.PredictedAt.Value >= request.PredictedAtFrom.Value);

                if (request.PredictedAtTo.HasValue)
                    filtered = filtered.Where(x => x.PredictedAt.HasValue && x.PredictedAt.Value <= request.PredictedAtTo.Value);

                if (request.MinConfidenceScore.HasValue)
                    filtered = filtered.Where(x => x.ConfidenceScore >= request.MinConfidenceScore.Value);

                if (request.MaxConfidenceScore.HasValue)
                    filtered = filtered.Where(x => x.ConfidenceScore <= request.MaxConfidenceScore.Value);

                if (!string.IsNullOrWhiteSpace(request.SummaryComment))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.SummaryComment, request.SummaryComment));

                var sorted = SortPredictionTickets(filtered, request);
                if (sorted is null)
                {
                    return Results.BadRequest(new[]
                    {
                        "SortBy must be one of: predictedAt, confidenceScore, ticketStatus, evaluationStatus."
                    });
                }

                return Results.Ok(ToPagedResponse(
                    sorted,
                    page,
                    pageSize,
                    x => new PredictionTicketSummaryResponse(
                        x.PredictionTicketId,
                        x.RaceId,
                        x.PredictorType,
                        x.PredictorId,
                        x.ConfidenceScore,
                        x.SummaryComment,
                        x.PredictedAt,
                        (ApiContracts.TicketStatus)(int)x.TicketStatus,
                        (ApiContracts.EvaluationStatus)(int)x.EvaluationStatus,
                        x.Marks.Count)));
            })
            .WithName("SearchPredictionTickets")
            .WithTags("Prediction API")
            .Produces<PagedResponse<PredictionTicketSummaryResponse>>(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .WithOpenApi();

        app.MapGet("/api/horses/{horseId}",
            [SwaggerOperation(Summary = "Get horse profile", Description = "Returns horse profile read model")]
        async (string horseId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<AppReadModels.HorseReadModel>(horseId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.HorseId))
                    return Results.NotFound();

                return Results.Ok(ToAgentHorse(readModel));
            })
            .WithName("GetHorseProfile")
            .WithTags("Horse API")
            .Produces<ApiContracts.HorseReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/horses",
            [SwaggerOperation(Summary = "Search horses", Description = "Returns paged horse summaries filtered by identifiers, names, sex, birth date and aliases")]
        async ([AsParameters] SearchHorsesRequest request,
                IDbContextProvider<EventStoreDbContext> dbContextProvider,
                CancellationToken cancellationToken) =>
            {
                var page = request.Page ?? 1;
                var pageSize = request.PageSize ?? 20;
                var pagingError = ValidatePaging(page, pageSize);
                if (pagingError is not null)
                    return Results.BadRequest(new[] { pagingError });

                using var dbContext = dbContextProvider.CreateContext();
                var allHorses = await dbContext.Set<AppReadModels.HorseReadModel>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IEnumerable<AppReadModels.HorseReadModel> filtered = allHorses;

                if (!string.IsNullOrWhiteSpace(request.HorseId))
                    filtered = filtered.Where(x => string.Equals(x.HorseId, request.HorseId, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.Query))
                {
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.RegisteredName, request.Query)
                        || ContainsIgnoreCase(x.NormalizedName, request.Query)
                        || x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.Query)));
                }

                if (!string.IsNullOrWhiteSpace(request.RegisteredName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.RegisteredName, request.RegisteredName));

                if (!string.IsNullOrWhiteSpace(request.NormalizedName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.NormalizedName, request.NormalizedName));

                if (!string.IsNullOrWhiteSpace(request.SexCode))
                    filtered = filtered.Where(x => string.Equals(x.SexCode, request.SexCode, StringComparison.OrdinalIgnoreCase));

                if (request.BirthDateFrom.HasValue)
                    filtered = filtered.Where(x => x.BirthDate.HasValue && x.BirthDate.Value >= request.BirthDateFrom.Value);

                if (request.BirthDateTo.HasValue)
                    filtered = filtered.Where(x => x.BirthDate.HasValue && x.BirthDate.Value <= request.BirthDateTo.Value);

                if (!string.IsNullOrWhiteSpace(request.AliasValue))
                    filtered = filtered.Where(x => x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.AliasValue)));

                var sorted = SortHorses(filtered, request);
                if (sorted is null)
                {
                    return Results.BadRequest(new[]
                    {
                        "SortBy must be one of: registeredName, normalizedName, birthDate."
                    });
                }

                return Results.Ok(ToPagedResponse(
                    sorted,
                    page,
                    pageSize,
                    x => new HorseSummaryResponse(
                        x.HorseId,
                        x.RegisteredName,
                        x.NormalizedName,
                        x.SexCode,
                        x.BirthDate,
                        x.Aliases.Count)));
            })
            .WithName("SearchHorses")
            .WithTags("Horse API")
            .Produces<PagedResponse<HorseSummaryResponse>>(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .WithOpenApi();

        app.MapGet("/api/horses/{horseId}/race-history",
            [SwaggerOperation(Summary = "Get horse race history", Description = "Returns race history read model for a horse")]
        async (string horseId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<AppReadModels.HorseRaceHistoryReadModel>(horseId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.HorseId))
                    return Results.NotFound();

                return Results.Ok(ToAgentHorseRaceHistory(readModel));
            })
            .WithName("GetHorseRaceHistory")
            .WithTags("Horse API")
            .Produces<ApiContracts.HorseRaceHistoryReadModel>(StatusCodes.Status200OK)
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
            .WithTags("Horse API")
            .Produces<HorseWeightHistoryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/jockeys/{jockeyId}/race-history",
            [SwaggerOperation(Summary = "Get jockey race history", Description = "Returns race history read model for a jockey")]
        async (string jockeyId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.JockeyId))
                    return Results.NotFound();

                return Results.Ok(ToAgentJockeyRaceHistory(readModel));
            })
            .WithName("GetJockeyRaceHistory")
            .WithTags("Jockey API")
            .Produces<ApiContracts.JockeyRaceHistoryReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/jockeys/{jockeyId}",
            [SwaggerOperation(Summary = "Get jockey profile", Description = "Returns jockey profile read model")]
        async (string jockeyId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
            {
                var query = new ReadModelByIdQuery<AppReadModels.JockeyReadModel>(jockeyId);
                var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                if (readModel is null || string.IsNullOrEmpty(readModel.JockeyId))
                    return Results.NotFound();

                return Results.Ok(ToAgentJockey(readModel));
            })
            .WithName("GetJockeyProfile")
            .WithTags("Jockey API")
            .Produces<ApiContracts.JockeyReadModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/jockeys",
            [SwaggerOperation(Summary = "Search jockeys", Description = "Returns paged jockey summaries filtered by identifiers, names, affiliation and aliases")]
        async ([AsParameters] SearchJockeysRequest request,
                IDbContextProvider<EventStoreDbContext> dbContextProvider,
                CancellationToken cancellationToken) =>
            {
                var page = request.Page ?? 1;
                var pageSize = request.PageSize ?? 20;
                var pagingError = ValidatePaging(page, pageSize);
                if (pagingError is not null)
                    return Results.BadRequest(new[] { pagingError });

                using var dbContext = dbContextProvider.CreateContext();
                var allJockeys = await dbContext.Set<AppReadModels.JockeyReadModel>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IEnumerable<AppReadModels.JockeyReadModel> filtered = allJockeys;

                if (!string.IsNullOrWhiteSpace(request.JockeyId))
                    filtered = filtered.Where(x => string.Equals(x.JockeyId, request.JockeyId, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.Query))
                {
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.DisplayName, request.Query)
                        || ContainsIgnoreCase(x.NormalizedName, request.Query)
                        || x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.Query)));
                }

                if (!string.IsNullOrWhiteSpace(request.DisplayName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.DisplayName, request.DisplayName));

                if (!string.IsNullOrWhiteSpace(request.NormalizedName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.NormalizedName, request.NormalizedName));

                if (!string.IsNullOrWhiteSpace(request.AffiliationCode))
                    filtered = filtered.Where(x => string.Equals(x.AffiliationCode, request.AffiliationCode, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.AliasValue))
                    filtered = filtered.Where(x => x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.AliasValue)));

                var sorted = SortJockeys(filtered, request);
                if (sorted is null)
                {
                    return Results.BadRequest(new[]
                    {
                        "SortBy must be one of: displayName, normalizedName, affiliationCode."
                    });
                }

                return Results.Ok(ToPagedResponse(
                    sorted,
                    page,
                    pageSize,
                    x => new JockeySummaryResponse(
                        x.JockeyId,
                        x.DisplayName,
                        x.NormalizedName,
                        x.AffiliationCode,
                        x.Aliases.Count)));
            })
            .WithName("SearchJockeys")
            .WithTags("Jockey API")
            .Produces<PagedResponse<JockeySummaryResponse>>(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
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
            .WithTags("Trainer API")
            .Produces<TrainerProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapGet("/api/trainers",
            [SwaggerOperation(Summary = "Search trainers", Description = "Returns paged trainer summaries filtered by identifiers, names, affiliation and aliases")]
        async ([AsParameters] SearchTrainersRequest request,
                IDbContextProvider<EventStoreDbContext> dbContextProvider,
                CancellationToken cancellationToken) =>
            {
                var page = request.Page ?? 1;
                var pageSize = request.PageSize ?? 20;
                var pagingError = ValidatePaging(page, pageSize);
                if (pagingError is not null)
                    return Results.BadRequest(new[] { pagingError });

                using var dbContext = dbContextProvider.CreateContext();
                var allTrainers = await dbContext.Set<TrainerReadModel>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IEnumerable<TrainerReadModel> filtered = allTrainers;

                if (!string.IsNullOrWhiteSpace(request.TrainerId))
                    filtered = filtered.Where(x => string.Equals(x.TrainerId, request.TrainerId, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.Query))
                {
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.DisplayName, request.Query)
                        || ContainsIgnoreCase(x.NormalizedName, request.Query)
                        || x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.Query)));
                }

                if (!string.IsNullOrWhiteSpace(request.DisplayName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.DisplayName, request.DisplayName));

                if (!string.IsNullOrWhiteSpace(request.NormalizedName))
                    filtered = filtered.Where(x => ContainsIgnoreCase(x.NormalizedName, request.NormalizedName));

                if (!string.IsNullOrWhiteSpace(request.AffiliationCode))
                    filtered = filtered.Where(x => string.Equals(x.AffiliationCode, request.AffiliationCode, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(request.AliasValue))
                    filtered = filtered.Where(x => x.Aliases.Any(a => ContainsIgnoreCase(a.AliasValue, request.AliasValue)));

                var sorted = SortTrainers(filtered, request);
                if (sorted is null)
                {
                    return Results.BadRequest(new[]
                    {
                        "SortBy must be one of: displayName, normalizedName, affiliationCode."
                    });
                }

                return Results.Ok(ToPagedResponse(
                    sorted,
                    page,
                    pageSize,
                    x => new TrainerSummaryResponse(
                        x.TrainerId,
                        x.DisplayName,
                        x.NormalizedName,
                        x.AffiliationCode,
                        x.Aliases.Count)));
            })
            .WithName("SearchTrainers")
            .WithTags("Trainer API")
            .Produces<PagedResponse<TrainerSummaryResponse>>(StatusCodes.Status200OK)
            .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
            .WithOpenApi();

        writeGroup.MapPost("/memos",
            [SwaggerOperation(Summary = "Create memo", Description = "Creates a memo that can be attached to any combination of subjects (horse, trainer, jockey, race)")]
        async (CreateMemoRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
            {
                if (request.Subjects is null || request.Subjects.Count == 0)
                    return Results.BadRequest(new[] { "At least one subject is required." });

                var memoId = string.IsNullOrWhiteSpace(request.MemoId)
                    ? MemoId.New : new MemoId(request.MemoId);

                var subjects = request.Subjects
                    .Select(s => new MemoSubject(Enum.Parse<MemoSubjectType>(s.SubjectType, ignoreCase: true), s.SubjectId))
                    .ToList();

                var links = (request.Links ?? Array.Empty<MemoLinkDto>())
                    .Select(l => new MemoLink(l.LinkId, Enum.Parse<MemoLinkType>(l.LinkType, ignoreCase: true), l.Title, l.Url, l.StorageKey))
                    .ToList();

                var command = new CreateMemoCommand(
                    memoId,
                    request.AuthorId,
                    request.MemoType,
                    request.Content,
                    request.CreatedAt,
                    subjects,
                    links);

                try
                {
                    var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                    return result.IsSuccess
                        ? Results.Created($"/api/memos/{memoId.Value}", new { MemoId = memoId.Value })
                        : Results.BadRequest(new[] { "Command execution failed." });
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Memo is already created.", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { ex.Message });
                }
            })
            .WithName("CreateMemo")
            .WithTags("Memo API")
            .Produces(StatusCodes.Status201Created)
            .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
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
            .WithTags("Memo API")
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
            .WithTags("Memo API")
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
            .WithTags("Memo API")
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
            .WithTags("Memo API")
            .Produces<IReadOnlyList<MemoResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        // ------------------------------------------------------------------ //
        // ML予測 API
        // ------------------------------------------------------------------ //

        app.MapGet("/api/races/{raceId}/ml-prediction",
            [SwaggerOperation(Summary = "ML予測", Description = "ML.NETモデルを使って出走馬の予測着順を返します。訓練済みモデルがない場合は統計スコアで代替します。")]
        async (string raceId, IQueryProcessor queryProcessor, IRacePredictor predictor, CancellationToken cancellationToken) =>
            {
                var raceQuery = new ReadModelByIdQuery<AppReadModels.RacePredictionContextReadModel>(raceId);
                var raceContext = await queryProcessor.ProcessAsync(raceQuery, cancellationToken).ConfigureAwait(false);

                if (raceContext is null || string.IsNullOrEmpty(raceContext.RaceId))
                    return Results.NotFound();

                var result = await predictor.PredictAsync(
                    raceContext,
                    async (horseId, ct) => await queryProcessor.ProcessAsync(
                        new ReadModelByIdQuery<AppReadModels.HorseRaceHistoryReadModel>(horseId), ct).ConfigureAwait(false),
                    async (jockeyId, ct) => await queryProcessor.ProcessAsync(
                        new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId), ct).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);

                var response = new ApiContracts.MlPredictionResponse(
                    result.RaceId,
                    result.Rankings.Select(r => new ApiContracts.MlHorsePrediction(
                        r.EntryId, r.HorseId, r.HorseNumber, r.PredictedScore, r.PredictedRank)).ToList());

                return Results.Ok(response);
            })
            .WithName("GetMlPrediction")
            .WithTags("Race API")
            .Produces<ApiContracts.MlPredictionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        app.MapPost("/api/ml/train",
            [SwaggerOperation(Summary = "ML再訓練", Description = "過去レース結果を使ってML.NETモデルを再訓練します。")]
        async (IQueryProcessor queryProcessor, IDbContextProvider<EventStoreDbContext> dbContextProvider,
                IRacePredictor predictor, CancellationToken cancellationToken) =>
            {
                using var dbContext = dbContextProvider.CreateContext();
                var allRaces = await dbContext.Set<RaceResultViewReadModel>()
                    .AsNoTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var finishedRaces = allRaces.Where(r => r.Status == HorseRacingPrediction.Domain.Races.RaceStatus.ResultDeclared).ToList();

                if (finishedRaces.Count == 0)
                    return Results.BadRequest(new[] { "訓練に使用できる完了済みレースがありません。" });

                await predictor.TrainAsync(
                    finishedRaces,
                    async (raceId, ct) => await queryProcessor.ProcessAsync(
                        new ReadModelByIdQuery<AppReadModels.RacePredictionContextReadModel>(raceId), ct).ConfigureAwait(false),
                    async (horseId, ct) => await queryProcessor.ProcessAsync(
                        new ReadModelByIdQuery<AppReadModels.HorseRaceHistoryReadModel>(horseId), ct).ConfigureAwait(false),
                    async (jockeyId, ct) => await queryProcessor.ProcessAsync(
                        new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId), ct).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);

                return Results.Ok(new { TrainedRaceCount = finishedRaces.Count, IsModelTrained = predictor.IsModelTrained });
            })
            .WithName("TrainMlModel")
            .WithTags("Race API")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithOpenApi();

        return app;
    }

    private static bool ContainsIgnoreCase(string? value, string searchTerm)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

    private static async Task EnsureRelatedSubjectsAsync(
        RegisterEntryRequest request,
        ICommandBus commandBus,
        IDbContextProvider<EventStoreDbContext> dbContextProvider,
        CancellationToken cancellationToken)
    {
        using var dbContext = dbContextProvider.CreateContext();

        var horseExists = await dbContext.Set<AppReadModels.HorseReadModel>()
            .AsNoTracking()
            .AnyAsync(x => x.HorseId == request.HorseId, cancellationToken)
            .ConfigureAwait(false);

        if (!horseExists)
        {
            var horseName = string.IsNullOrWhiteSpace(request.HorseName) ? request.HorseId : request.HorseName;
            var normalizedHorseName = NormalizeDisplayName(horseName);
            var registerHorse = new RegisterHorseCommand(
                new HorseId(request.HorseId),
                horseName,
                normalizedHorseName,
                request.SexCode,
                birthDate: null);

            var horseResult = await commandBus.PublishAsync(registerHorse, cancellationToken).ConfigureAwait(false);
            if (!horseResult.IsSuccess)
            {
                throw new InvalidOperationException($"Horse auto-registration failed. HorseId={request.HorseId}");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.JockeyId))
        {
            var jockeyExists = await dbContext.Set<AppReadModels.JockeyReadModel>()
                .AsNoTracking()
                .AnyAsync(x => x.JockeyId == request.JockeyId, cancellationToken)
                .ConfigureAwait(false);

            if (!jockeyExists)
            {
                var jockeyName = string.IsNullOrWhiteSpace(request.JockeyName) ? request.JockeyId : request.JockeyName;
                var normalizedJockeyName = NormalizeDisplayName(jockeyName);
                var registerJockey = new RegisterJockeyCommand(
                    new JockeyId(request.JockeyId),
                    jockeyName,
                    normalizedJockeyName,
                    affiliationCode: null);

                var jockeyResult = await commandBus.PublishAsync(registerJockey, cancellationToken).ConfigureAwait(false);
                if (!jockeyResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Jockey auto-registration failed. JockeyId={request.JockeyId}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(request.TrainerId))
        {
            var trainerExists = await dbContext.Set<TrainerReadModel>()
                .AsNoTracking()
                .AnyAsync(x => x.TrainerId == request.TrainerId, cancellationToken)
                .ConfigureAwait(false);

            if (!trainerExists)
            {
                var trainerName = string.IsNullOrWhiteSpace(request.TrainerName) ? request.TrainerId : request.TrainerName;
                var normalizedTrainerName = NormalizeDisplayName(trainerName);
                var registerTrainer = new RegisterTrainerCommand(
                    new TrainerId(request.TrainerId),
                    trainerName,
                    normalizedTrainerName,
                    affiliationCode: null);

                var trainerResult = await commandBus.PublishAsync(registerTrainer, cancellationToken).ConfigureAwait(false);
                if (!trainerResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Trainer auto-registration failed. TrainerId={request.TrainerId}");
                }
            }
        }
    }

    private static string NormalizeDisplayName(string value)
        => string.Join(
            string.Empty,
            value
                .Trim()
                .Where(c => !char.IsWhiteSpace(c)));

    private static string? ValidatePaging(int page, int pageSize)
    {
        if (page < 1)
            return "Page must be greater than or equal to 1.";

        if (pageSize is < 1 or > 100)
            return "PageSize must be between 1 and 100.";

        return null;
    }

    private static PagedResponse<TResponse> ToPagedResponse<TReadModel, TResponse>(
        IEnumerable<TReadModel> source,
        int page,
        int pageSize,
        Func<TReadModel, TResponse> selector)
    {
        var totalCount = source.Count();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var items = source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToList();

        return new PagedResponse<TResponse>(items, page, pageSize, totalCount, totalPages);
    }

    private static IOrderedEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel>? SortHorses(
        IEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel> source,
        SearchHorsesRequest request)
        => (request.SortBy ?? "registeredName").ToLowerInvariant() switch
        {
            "registeredname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.RegisteredName).ThenByDescending(x => x.HorseId)
                : source.OrderBy(x => x.RegisteredName).ThenBy(x => x.HorseId),
            "normalizedname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.NormalizedName).ThenByDescending(x => x.HorseId)
                : source.OrderBy(x => x.NormalizedName).ThenBy(x => x.HorseId),
            "birthdate" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.BirthDate).ThenByDescending(x => x.RegisteredName)
                : source.OrderBy(x => x.BirthDate).ThenBy(x => x.RegisteredName),
            _ => null
        };

    private static IOrderedEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.JockeyReadModel>? SortJockeys(
        IEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.JockeyReadModel> source,
        SearchJockeysRequest request)
        => (request.SortBy ?? "displayName").ToLowerInvariant() switch
        {
            "displayname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.DisplayName).ThenByDescending(x => x.JockeyId)
                : source.OrderBy(x => x.DisplayName).ThenBy(x => x.JockeyId),
            "normalizedname" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.NormalizedName).ThenByDescending(x => x.JockeyId)
                : source.OrderBy(x => x.NormalizedName).ThenBy(x => x.JockeyId),
            "affiliationcode" => (request.SortDescending ?? false)
                ? source.OrderByDescending(x => x.AffiliationCode).ThenByDescending(x => x.DisplayName)
                : source.OrderBy(x => x.AffiliationCode).ThenBy(x => x.DisplayName),
            _ => null
        };

    private static IOrderedEnumerable<AppReadModels.TrainerReadModel>? SortTrainers(
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

    private static IOrderedEnumerable<AppReadModels.PredictionTicketReadModel>? SortPredictionTickets(
        IEnumerable<AppReadModels.PredictionTicketReadModel> source,
        SearchPredictionTicketsRequest request)
        => (request.SortBy ?? "predictedAt").ToLowerInvariant() switch
        {
            "predictedat" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.PredictedAt).ThenByDescending(x => x.PredictionTicketId)
                : source.OrderBy(x => x.PredictedAt).ThenBy(x => x.PredictionTicketId),
            "confidencescore" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.ConfidenceScore).ThenByDescending(x => x.PredictedAt)
                : source.OrderBy(x => x.ConfidenceScore).ThenBy(x => x.PredictedAt),
            "ticketstatus" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.TicketStatus).ThenByDescending(x => x.PredictedAt)
                : source.OrderBy(x => x.TicketStatus).ThenBy(x => x.PredictedAt),
            "evaluationstatus" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.EvaluationStatus).ThenByDescending(x => x.PredictedAt)
                : source.OrderBy(x => x.EvaluationStatus).ThenBy(x => x.PredictedAt),
            _ => null
        };

    private static ApiContracts.RacePredictionContextReadModel ToAgentRacePredictionContext(HorseRacingPrediction.Application.Queries.ReadModels.RacePredictionContextReadModel model)
        => new()
        {
            RaceId = model.RaceId,
            RaceDate = model.RaceDate,
            RacecourseCode = model.RacecourseCode,
            RaceNumber = model.RaceNumber,
            RaceName = model.RaceName,
            Status = (ApiContracts.RaceStatus)(int)model.Status,
            GradeCode = model.GradeCode,
            SurfaceCode = model.SurfaceCode,
            DistanceMeters = model.DistanceMeters,
            DirectionCode = model.DirectionCode,
            Entries = model.Entries.Select(x => new ApiContracts.RacePredictionContextEntry(x.EntryId, x.HorseId, x.HorseNumber, x.JockeyId, x.TrainerId, x.GateNumber, x.AssignedWeight, x.SexCode, x.Age, x.DeclaredWeight, x.DeclaredWeightDiff, x.RunningStyleCode)).ToList(),
            WeatherObservations = model.WeatherObservations.Select(x => new ApiContracts.WeatherObservationSnapshot(x.ObservationTime, x.WeatherCode, x.WeatherText, x.TemperatureCelsius, x.HumidityPercent, x.WindDirectionCode, x.WindSpeedMeterPerSecond)).ToList(),
            TrackConditionObservations = model.TrackConditionObservations.Select(x => new ApiContracts.TrackConditionSnapshot(x.ObservationTime, x.TurfConditionCode, x.DirtConditionCode, x.GoingDescriptionText)).ToList()
        };

    private static RaceEntryResponse ToRaceEntryResponse(
        HorseRacingPrediction.Application.Queries.ReadModels.RacePredictionContextEntry entry,
        string? horseName,
        string? jockeyName,
        string? trainerName,
        int? gateNumber)
        => new(
            entry.EntryId,
            entry.HorseId,
            horseName,
            entry.HorseNumber,
            entry.JockeyId,
            jockeyName,
            entry.TrainerId,
            trainerName,
            gateNumber,
            entry.AssignedWeight,
            entry.SexCode,
            entry.Age,
            entry.DeclaredWeight,
            entry.DeclaredWeightDiff,
            entry.RunningStyleCode);

    private static RaceEntryResponse ToRaceEntryResponse(AppReadModels.EntryResultSnapshot entryResult, string? horseId, int horseNumber, int? gateNumber, string? horseName)
        => new(
            entryResult.EntryId,
            horseId ?? string.Empty,
            horseName,
            horseNumber,
            null,
            null,
            null,
            null,
            gateNumber,
            null,
            null,
            null,
            null,
            null,
            null);

    private static RaceWeatherObservationResponse ToRaceWeatherObservationResponse(
        HorseRacingPrediction.Application.Queries.ReadModels.WeatherObservationSnapshot observation)
        => new(
            observation.ObservationTime,
            observation.WeatherCode,
            observation.WeatherText,
            observation.TemperatureCelsius,
            observation.HumidityPercent,
            observation.WindDirectionCode,
            observation.WindSpeedMeterPerSecond);

    private static RaceTrackConditionResponse ToRaceTrackConditionResponse(
        HorseRacingPrediction.Application.Queries.ReadModels.TrackConditionSnapshot condition)
        => new(
            condition.ObservationTime,
            condition.TurfConditionCode,
            condition.DirtConditionCode,
            condition.GoingDescriptionText);

    private static RaceEntryResultResponse ToRaceEntryResultResponse(AppReadModels.EntryResultSnapshot entryResult, string? horseId, int horseNumber, string? horseName)
        => new(
            entryResult.EntryId,
            horseId ?? string.Empty,
            horseName,
            horseNumber,
            entryResult.FinishPosition,
            entryResult.OfficialTime,
            entryResult.MarginText,
            entryResult.LastThreeFurlongTime,
            entryResult.AbnormalResultCode,
            entryResult.PrizeMoney,
            entryResult.CornerPositions);

    private static string? ResolveHorseId(IReadOnlyDictionary<string, string> entryHorseIdsByEntryId, string entryId, string? horseId)
        => !string.IsNullOrWhiteSpace(horseId)
            ? horseId
            : entryHorseIdsByEntryId.TryGetValue(entryId, out var fallbackHorseId)
                ? fallbackHorseId
                : null;

    private static int ResolveHorseNumber(IReadOnlyDictionary<string, int> entryHorseNumbersByEntryId, string entryId, int horseNumber)
        => horseNumber > 0
            ? horseNumber
            : entryHorseNumbersByEntryId.TryGetValue(entryId, out var fallbackHorseNumber)
                ? fallbackHorseNumber
                : 0;

    private static int? ResolveGateNumber(
        IReadOnlyDictionary<string, int> entryGateNumbersByEntryId,
        IReadOnlyDictionary<string, int> resultEntryGateNumbersByEntryId,
        string entryId,
        int horseNumber)
        => entryGateNumbersByEntryId.TryGetValue(entryId, out var gateNumber)
            ? gateNumber
            : resultEntryGateNumbersByEntryId.TryGetValue(entryId, out var fallbackGateNumber)
                ? fallbackGateNumber
                : ResolveGateNumberFromHorseNumber(horseNumber);

    private static int? ResolveGateNumberFromHorseNumber(int horseNumber)
        => horseNumber > 0
            ? (horseNumber + 1) / 2
            : null;

    private static string? ResolveHorseName(IReadOnlyDictionary<string, string> horseNamesById, string? horseId)
        => !string.IsNullOrWhiteSpace(horseId) && horseNamesById.TryGetValue(horseId, out var horseName)
            ? horseName
            : null;

    private static string? ResolveJockeyName(IReadOnlyDictionary<string, string> jockeyNamesById, string? jockeyId)
        => !string.IsNullOrWhiteSpace(jockeyId) && jockeyNamesById.TryGetValue(jockeyId, out var jockeyName)
            ? jockeyName
            : null;

    private static string? ResolveTrainerName(IReadOnlyDictionary<string, string> trainerNamesById, string? trainerId)
        => !string.IsNullOrWhiteSpace(trainerId) && trainerNamesById.TryGetValue(trainerId, out var trainerName)
            ? trainerName
            : null;

    private static RacePayoutResultResponse ToRacePayoutResultResponse(AppReadModels.PayoutResultSnapshot payoutResult)
        => new(
            payoutResult.DeclaredAt,
            payoutResult.WinPayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.PlacePayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.QuinellaPayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.ExactaPayouts.Select(ToRacePayoutEntryResponse).ToList(),
            payoutResult.TrifectaPayouts.Select(ToRacePayoutEntryResponse).ToList());

    private static RacePayoutEntryResponse ToRacePayoutEntryResponse(AppReadModels.PayoutEntrySnapshot payout)
        => new(payout.Combination, payout.Amount);

    private static RaceOddsResponse BuildUnavailableOddsResponse()
        => new(
            false,
            "保存済みオッズはまだ API ReadModel に保持されていません。",
            [],
            []);

    private static ApiContracts.HorseReadModel ToAgentHorse(HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel model)
        => new()
        {
            HorseId = model.HorseId,
            RegisteredName = model.RegisteredName,
            NormalizedName = model.NormalizedName,
            SexCode = model.SexCode,
            BirthDate = model.BirthDate,
            OwnerName = model.OwnerName,
            Aliases = model.Aliases.Select(x => new ApiContracts.HorseAliasEntry(x.AliasType, x.AliasValue, x.SourceName, x.IsPrimary)).ToList()
        };

    private static ApiContracts.JockeyReadModel ToAgentJockey(HorseRacingPrediction.Application.Queries.ReadModels.JockeyReadModel model)
        => new()
        {
            JockeyId = model.JockeyId,
            DisplayName = model.DisplayName,
            NormalizedName = model.NormalizedName,
            AffiliationCode = model.AffiliationCode,
            Aliases = model.Aliases.Select(x => new ApiContracts.JockeyAliasEntry(x.AliasType, x.AliasValue, x.SourceName, x.IsPrimary)).ToList()
        };

    private static ApiContracts.HorseRaceHistoryReadModel ToAgentHorseRaceHistory(HorseRacingPrediction.Application.Queries.ReadModels.HorseRaceHistoryReadModel model)
        => new()
        {
            HorseId = model.HorseId,
            Entries = model.Entries.Select(x => new ApiContracts.HorseRaceHistoryEntry(x.RaceId, x.EntryId, x.RaceDate, x.RacecourseCode, x.SurfaceCode, x.DistanceMeters, x.DirectionCode, x.GradeCode, x.GateNumber, x.AssignedWeight, x.DeclaredWeight, x.DeclaredWeightDiff, x.RunningStyleCode, x.JockeyId, x.TrainerId, x.FinishPosition, x.LastThreeFurlongTime, x.CornerPositions, x.PrizeMoney)).ToList()
        };

    private static ApiContracts.JockeyRaceHistoryReadModel ToAgentJockeyRaceHistory(HorseRacingPrediction.Application.Queries.ReadModels.JockeyRaceHistoryReadModel model)
        => new()
        {
            JockeyId = model.JockeyId,
            Entries = model.Entries.Select(x => new ApiContracts.JockeyRaceHistoryEntry(x.RaceId, x.EntryId, x.HorseId, x.RaceDate, x.RacecourseCode, x.SurfaceCode, x.DistanceMeters, x.DirectionCode, x.GradeCode, x.FinishPosition, x.PrizeMoney)).ToList()
        };
}
