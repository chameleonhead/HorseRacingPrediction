using EventFlow;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Domain.Memos;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Memos;

namespace HorseRacingPrediction.Api.Endpoints.Memos;


internal static class UpdateMemoEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/memos/{memoId}",
                    [SwaggerOperation(Summary = "Update memo", Description = "Updates content or links of an existing memo")]
        async (string memoId, UpdateMemoRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Memo is not { } memo)
                            return Results.BadRequest(new[] { "Command execution failed." });

                        var links = memo.Links?.Select(l =>
                            new MemoLink(l.LinkId, Enum.Parse<MemoLinkType>(l.LinkType, ignoreCase: true), l.Title, l.Url, l.StorageKey))
                            .ToList();

                        var command = new UpdateMemoCommand(new MemoId(memoId), memo.MemoType, memo.Content, links);
                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("UpdateMemo")
                    .WithTags("Memo API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
