using EventFlow;
using EventFlow.Queries;
using EventFlow.ReadStores;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Api.CollectionController;

public static class RaceOddsEndpointExtensions
{
    public static IEndpointRouteBuilder MapRaceOddsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/races/{raceId}/odds-snapshots", async (string raceId,
            RecordRaceOddsSnapshotRequest request, ICommandBus commands, CancellationToken token) =>
        {
            var entries = request.Entries ?? [];
            var observations = request.Observations;
            var errors = Validate(request.ObservedAt, entries, observations);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            await commands.PublishAsync(new RecordRaceOddsSnapshotCommand(new RaceId(raceId), request.ObservedAt,
                entries.Select(x => new RaceOddsEntry(x.HorseNumber, x.WinOdds, x.Popularity)).ToArray(),
                observations?.Select(x => new RaceOddsObservation(x.Market, x.Selection, x.Value,
                    x.Popularity)).ToArray()), token);
            return Results.Accepted();
        });
        endpoints.MapGet("/api/admin/races/{raceId}/odds-snapshots", async (string raceId,
            IQueryProcessor queries, CancellationToken token) =>
        {
            var model = await queries.ProcessAsync(new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId), token);
            return model is null ? Results.NotFound() : Results.Ok(model.OddsSnapshots);
        });
        return endpoints;
    }

    private static Dictionary<string, string[]> Validate(DateTimeOffset observedAt,
        IReadOnlyList<RaceOddsEntryRequest> entries,
        IReadOnlyList<RaceOddsObservationRequest>? observations)
    {
        var errors = new Dictionary<string, string[]>();
        if (observedAt == default)
            errors["observedAt"] = ["観測日時を指定してください。"];
        if (entries.Count == 0 && (observations is null || observations.Count == 0))
            errors["snapshot"] = ["単勝オッズまたは市場別オッズを1件以上指定してください。"];
        if (entries.Any(x => x is null || x.HorseNumber <= 0 || x.WinOdds <= 0)
            || entries.Where(x => x is not null).Select(x => x.HorseNumber).Distinct().Count() != entries.Count)
            errors["entries"] = ["馬番と単勝オッズは正の値とし、馬番を重複させないでください。"];
        if (observations is not null && (observations.Any(x => x is null || string.IsNullOrWhiteSpace(x.Market)
                || string.IsNullOrWhiteSpace(x.Selection) || x.Value <= 0)
            || observations.Where(x => x is not null)
                .Select(x => $"{x.Market.Trim().ToUpperInvariant()}\u001f{x.Selection.Trim().ToUpperInvariant()}")
                .Distinct(StringComparer.Ordinal).Count() != observations.Count))
            errors["observations"] = ["市場・選択肢・正のオッズを指定し、市場と選択肢の組を重複させないでください。"];
        return errors;
    }
}
