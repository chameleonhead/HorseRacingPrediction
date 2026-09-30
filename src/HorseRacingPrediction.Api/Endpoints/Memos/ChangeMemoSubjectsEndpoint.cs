using EventFlow;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Domain.Memos;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Memos;

namespace HorseRacingPrediction.Api.Endpoints.Memos;


internal static class ChangeMemoSubjectsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/memos/{memoId}/subjects",
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
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
