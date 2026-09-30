using EventFlow;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Domain.Memos;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Memos;

namespace HorseRacingPrediction.Api.Endpoints.Memos;


internal static class CreateMemoEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/memos",
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
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
