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
                    CollectionResourceType subjectType, string? query, int? page, int? pageSize,
                    IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
                {
                    if (subjectType is not (CollectionResourceType.Horse or CollectionResourceType.Jockey or CollectionResourceType.Trainer))
                        return Results.BadRequest(new[] { "対象種別は競走馬、騎手、調教師から選択してください。" });
                    var search = query?.Trim();
                    if (string.IsNullOrWhiteSpace(search))
                        return Results.BadRequest(new[] { "名称またはIDを入力してください。" });
                    var actualPage = Math.Max(1, page ?? 1);
                    var actualPageSize = Math.Clamp(pageSize ?? 25, 1, 50);
                    using var db = provider.CreateContext();
                    var searchPage = await SearchSubjectNamesAsync(db, subjectType, search, actualPage, actualPageSize, token)
                        .ConfigureAwait(false);
                    var all = await GetAllSubjectNamesAsync(db, subjectType, token).ConfigureAwait(false);
                    var items = searchPage.Rows
                        .Select(row => BuildSubjectNameNormalizationCandidate(subjectType, row, all)).ToArray();
                    return Results.Ok(new SubjectNameNormalizationPageDto(items, searchPage.TotalCount, actualPage, actualPageSize));
                });
    }
}
