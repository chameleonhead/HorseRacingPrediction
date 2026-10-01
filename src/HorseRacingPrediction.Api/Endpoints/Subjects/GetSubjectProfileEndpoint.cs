using EventFlow.Queries;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.AspNetCore.Mvc;

using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Api.Endpoints.Subjects;

internal static class GetSubjectProfileEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v2/admin/subjects/{kind}/{subjectId}/profiles/current",
            async ([AsParameters] GetSubjectProfileRequest request, [FromServices] IQueryProcessor queries, CancellationToken token) =>
            {
                var kind = request.Kind;
                var subjectId = request.SubjectId;
                if (await SubjectCollectionEndpointMappings.ResolveAsync(kind, subjectId, queries, token) is null)
                    return Results.NotFound();
                var profile = await queries.ProcessAsync(
                    new ReadModelByIdQuery<JraSubjectProfileReadModel>(subjectId), token);
                return profile is null || string.IsNullOrEmpty(profile.SubjectId)
                    ? Results.NotFound()
                    : Results.Ok(new GetSubjectProfileResponse(new JraSubjectProfileDto(kind, profile.Name, profile.SourceIdentity,
                        profile.SourceUrl, profile.Fields, profile.AcquiredAt)));
            })
            .AddEndpointFilter<RaceWriteEndpointFilter>()
            .Produces<GetSubjectProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
