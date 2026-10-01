using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.SubjectNameNormalizationService;


internal static class GetSubjectNameNormalizationEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/admin/repairs/subject-name-normalization", async (
                    [AsParameters] GetSubjectNameNormalizationRequest request,
                    IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
                {
                    var subjectType = request.SubjectType;
                    var query = request.Query;
                    if (subjectType is not (CollectionResourceType.Horse or CollectionResourceType.Jockey or CollectionResourceType.Trainer))
                        return Results.BadRequest(new[] { "対象種別は競走馬、騎手、調教師から選択してください。" });
                    var search = query?.Trim();
                    if (string.IsNullOrWhiteSpace(search))
                        return Results.BadRequest(new[] { "名称またはIDを入力してください。" });
                    var actualPage = Math.Max(1, request.Page ?? 1);
                    var actualPageSize = Math.Clamp(request.PageSize ?? 25, 1, 50);
                    using var db = provider.CreateContext();
                    var searchPage = await SearchSubjectNamesAsync(db, subjectType, search, actualPage, actualPageSize, token)
                        .ConfigureAwait(false);
                    var all = await GetAllSubjectNamesAsync(db, subjectType, token).ConfigureAwait(false);
                    var items = searchPage.Rows
                        .Select(row => BuildSubjectNameNormalizationCandidate(subjectType, row, all)).ToArray();
                    return Results.Ok(new GetSubjectNameNormalizationResponse(
                        new SubjectNameNormalizationPageDto(items, searchPage.TotalCount, actualPage, actualPageSize)));
                })
                .Produces<GetSubjectNameNormalizationResponse>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest);
    }
}
