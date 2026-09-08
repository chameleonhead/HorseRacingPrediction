using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public static class RaceReacquisitionEndpointExtensions
{
    public static IEndpointRouteBuilder MapRaceReacquisitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/races/{raceId}/reacquisition");
        group.MapGet("", async (string raceId, ProcessingStateStore store, CancellationToken token) =>
        {
            var job = await store.GetRaceReacquisitionAsync(raceId, token);
            return job is null ? Results.NoContent() : Results.Ok(job);
        });
        group.MapPost("", async (string raceId, IQueryProcessor queries, ProcessingStateStore store,
            HttpContext context, CancellationToken token) =>
        {
            var race = await queries.ProcessAsync(new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId), token);
            if (race is null || string.IsNullOrEmpty(race.RaceId)) return Results.NotFound();
            var course = ResolveCourse(race.RacecourseCode);
            if (race.RaceDate is null || race.RaceNumber is not (>= 1 and <= 12) || course is null)
                return Results.BadRequest(new[] { "JRAの開催日・競馬場・レース番号を確認できません。" });
            var id = await store.RequestRaceReacquisitionAsync(new(raceId, race.RaceDate.Value,
                course, race.RaceNumber.Value),
                context.User.Identity?.Name ?? "Admin API", DateTimeOffset.UtcNow, token);
            return Results.Accepted($"/api/admin/jobs/{Uri.EscapeDataString(id)}", new { jobId = id });
        });
        return endpoints;
    }

    internal static string? ResolveCourse(string? value) => value?.ToUpperInvariant() switch
    {
        "SAPPORO" or "札幌" => "札幌",
        "HAKODATE" or "函館" => "函館",
        "FUKUSHIMA" or "福島" => "福島",
        "NIIGATA" or "新潟" => "新潟",
        "TOKYO" or "東京" => "東京",
        "NAKAYAMA" or "中山" => "中山",
        "CHUKYO" or "中京" => "中京",
        "KYOTO" or "京都" => "京都",
        "HANSHIN" or "阪神" => "阪神",
        "KOKURA" or "小倉" => "小倉",
        _ => null
    };
}
