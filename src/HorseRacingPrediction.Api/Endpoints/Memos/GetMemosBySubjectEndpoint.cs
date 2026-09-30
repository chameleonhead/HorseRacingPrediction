using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Memos;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Memos;

namespace HorseRacingPrediction.Api.Endpoints.Memos;


internal static class GetMemosBySubjectEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/memos/by-subject/{subjectType}/{subjectId}",
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

                        var response = readModel.Memos.Select(m => new MemoDto(
                            m.MemoId, m.AuthorId, m.MemoType, m.Content, m.CreatedAt,
                            m.Subjects.Select(s => new MemoSubjectDto(s.SubjectType, s.SubjectId)).ToList(),
                            m.Links.Select(l => new MemoLinkDto(l.LinkId, l.LinkType, l.Title, l.Url, l.StorageKey)).ToList()))
                            .ToList();

                        return Results.Ok(response);
                    })
                    .WithName("GetMemosBySubject")
                    .WithTags("Memo API")
                    .Produces<IReadOnlyList<MemoDto>>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
