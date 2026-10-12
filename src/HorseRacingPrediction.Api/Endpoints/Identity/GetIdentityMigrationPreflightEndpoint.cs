using System.Data.Common;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.Endpoints.Identity;

internal static class GetIdentityMigrationPreflightEndpoint
{
    internal static void Map(RouteGroupBuilder endpoints) =>
        endpoints.MapGet("/v2/admin/identity-migration-preflight", HandleAsync)
            .Produces<IdentityMigrationPreflightResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> HandleAsync(
        [FromQuery] string? cursor,
        [FromServices] IdentityMigrationPreflightService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var page = await service.GetPageAsync(cursor, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new IdentityMigrationPreflightResponse(page));
        }
        catch (IdentityMigrationPreflightInvalidCursorException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid continuation cursor.");
        }
        catch (IdentityMigrationPreflightBusyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Preflight is busy.");
        }
        catch (IdentityMigrationPreflightDeadlineException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Preflight deadline exceeded.");
        }
        catch (IdentityMigrationPreflightUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Preflight store unavailable.");
        }
        catch (Exception exception) when (exception is SqliteException or DbException or InvalidOperationException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Preflight store unavailable.");
        }
    }
}
