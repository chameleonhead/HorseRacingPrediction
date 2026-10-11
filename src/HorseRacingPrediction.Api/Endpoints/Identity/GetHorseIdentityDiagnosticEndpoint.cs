using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using System.Data.Common;

namespace HorseRacingPrediction.Api.Endpoints.Identity;

internal static class GetHorseIdentityDiagnosticEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/v2/admin/identity-diagnostics/horses/{horseId}", HandleAsync)
            .Produces<GetHorseIdentityDiagnosticResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string horseId,
        [FromServices] HorseIdentityDiagnosticService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await service.GetAsync(horseId, cancellationToken).ConfigureAwait(false);
            return result is null
                ? Results.NotFound()
                : Results.Ok(new GetHorseIdentityDiagnosticResponse(result));
        }
        catch (InvalidHorseIdentityDiagnosticIdException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid horse ID.");
        }
        catch (HorseIdentityDiagnosticBusyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Diagnostic is busy.");
        }
        catch (HorseIdentityDiagnosticDeadlineException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Diagnostic deadline exceeded.");
        }
        catch (Exception exception) when (exception is SqliteException or DbException or InvalidOperationException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Diagnostic store unavailable.");
        }
    }
}
