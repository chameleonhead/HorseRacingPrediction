using EventFlow;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Contracts.Memos;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Memos;


internal static class DeleteMemoEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/memos/{memoId}",
                    [SwaggerOperation(Summary = "Delete memo", Description = "Deletes a memo")]
        async ([AsParameters] DeleteMemoRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var memoId = request.MemoId;
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
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
