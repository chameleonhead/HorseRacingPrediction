using HorseRacingPrediction.AgentClient.Scheduling;

namespace HorseRacingPrediction.Collector.Scheduling;

public static class AgentCollectionStatusEndpointExtensions
{
    public static IEndpointRouteBuilder MapAgentCollectionStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/agent/race-collection-statuses",
            async (
                DateOnly from,
                DateOnly to,
                ProcessingStateStore stateStore,
                HorseRacingPrediction.ApiClient.IRaceQueryService raceQueryService,
                CancellationToken cancellationToken) =>
            {
                if (from > to)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["from"] = ["from は to 以下である必要があります。"]
                    });
                }

                var items = await stateStore.GetRaceDataCollectionStatusesAsync(from, to, cancellationToken).ConfigureAwait(false);
                var raceIds = items
                    .Where(x => !string.IsNullOrWhiteSpace(x.RaceId))
                    .Select(x => x.RaceId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var raceNamesById = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var raceId in raceIds)
                {
                    var context = await raceQueryService.GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
                    raceNamesById[raceId] = context?.RaceName;
                }

                var normalized = items.Select(item =>
                {
                    string? raceName = item.RaceName;
                    if (!string.IsNullOrWhiteSpace(item.RaceId)
                        && raceNamesById.TryGetValue(item.RaceId!, out var resolvedRaceName)
                        && !string.IsNullOrWhiteSpace(resolvedRaceName))
                    {
                        raceName = resolvedRaceName;
                    }

                    if (IsPlaceholderRaceName(raceName))
                    {
                        raceName = $"{item.RaceNumber}R";
                    }

                    return item with
                    {
                        RaceName = raceName
                    };
                }).ToList();

                return Results.Ok(normalized);
            });

        return endpoints;
    }

    private static bool IsPlaceholderRaceName(string? raceName)
    {
        if (string.IsNullOrWhiteSpace(raceName))
        {
            return true;
        }

        var normalized = raceName.Trim();
        return normalized.Equals("JRA 日本中央競馬会", StringComparison.Ordinal)
            || normalized.Equals("日本中央競馬会", StringComparison.Ordinal)
            || normalized.Equals("JRA", StringComparison.OrdinalIgnoreCase);
    }
}