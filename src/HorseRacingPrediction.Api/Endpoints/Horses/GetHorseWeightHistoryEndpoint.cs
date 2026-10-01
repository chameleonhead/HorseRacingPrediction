using EventFlow.Queries;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Horses;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class GetHorseWeightHistoryEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/horses/{horseId}/weight-history",
                    [SwaggerOperation(Summary = "Get horse weight history", Description = "Returns horse body weight history across races")]
        async ([AsParameters] GetHorseWeightHistoryRequest request, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var horseId = request.HorseId;
                        var query = new ReadModelByIdQuery<HorseWeightHistoryReadModel>(horseId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.HorseId))
                            return Results.NotFound();

                        var response = new HorseWeightHistoryDto(
                            readModel.HorseId,
                            readModel.WeightHistory
                                .OrderByDescending(w => w.RecordedAt)
                                .Select(w => new HorseWeightEntryDto(w.RaceId, w.EntryId, w.RecordedAt, w.DeclaredWeight, w.DeclaredWeightDiff))
                                .ToList());

                        return Results.Ok(new GetHorseWeightHistoryResponse(response));
                    })
                    .AddEndpointFilter<RacePredictionReadEndpointFilter>()
                    .WithName("GetHorseWeightHistory")
                    .WithTags("Horse API")
                    .Produces<GetHorseWeightHistoryResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
