using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public static class RaceDayReacquisitionEndpointExtensions
{
    public static IEndpointRouteBuilder MapRaceDayReacquisitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/race-days/reacquisition");
        group.MapGet("", async (DateOnly raceDate, ProcessingStateStore store, CancellationToken token) =>
        {
            var job = await store.GetRaceDayReacquisitionAsync(raceDate, token).ConfigureAwait(false);
            return job is null ? Results.NoContent() : Results.Ok(job);
        });
        group.MapPost("", async (
            RaceDayReacquisitionRequest request,
            ProcessingStateStore store,
            HttpContext context,
            CancellationToken token) =>
        {
            var todayJst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo")).Date);
            if (request.RaceDate > todayJst)
                return Results.BadRequest(new[] { "未来の開催日は全データ再取得の対象にできません。" });

            var id = await store.RequestRaceDayReacquisitionAsync(
                new RaceDayReacquisitionPayload(request.RaceDate, "JRA", request.Reason),
                context.User.Identity?.Name ?? "Admin API",
                DateTimeOffset.UtcNow,
                token).ConfigureAwait(false);
            return Results.Accepted($"/api/admin/jobs/{Uri.EscapeDataString(id)}", new RaceDayReacquisitionResponse(id));
        });
        return endpoints;
    }
}

public sealed record RaceDayReacquisitionRequest(DateOnly RaceDate, string? Reason = null);
public sealed record RaceDayReacquisitionResponse(string JobId);
