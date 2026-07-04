using HorseRacingPrediction.Scraping.JraNavigation;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class JraHistoricalRaceReferenceCollector : IHistoricalRaceReferenceCollector
{
    private readonly ILogger<JraHistoricalRaceReferenceCollector> _logger;

    public JraHistoricalRaceReferenceCollector(ILogger<JraHistoricalRaceReferenceCollector> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<HistoricalRaceReference>> CollectAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default)
    {
        var resolvedRacecourse = JraRacecourseResolver.ResolveDisplayName(racecourse)
            ?? throw new InvalidOperationException($"JRA 競馬場名へ変換できませんでした: {racecourse}");

        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);

        var raceCard = await taskAgent
            .RequestRaceCardAsync(raceDate, resolvedRacecourse, raceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (!raceCard.Success)
        {
            throw new InvalidOperationException(
                $"出馬表ページを開けませんでした。Date={raceDate:yyyy-MM-dd} Racecourse={resolvedRacecourse} RaceNumber={raceNumber} Error={raceCard.Error}");
        }

        await taskAgent.FollowStructuredNextLinkAsync("過去の成績", cancellationToken).ConfigureAwait(false);
        var snapshot = await taskAgent.GetPageSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var references = HistoricalRaceReferenceParser.Parse(snapshot, raceDate);

        _logger.LogInformation(
            "[過去データ補完] 出馬表から過去レース参照を抽出しました。Date={Date} Racecourse={Racecourse} RaceNumber={RaceNumber} Count={Count}",
            raceDate,
            resolvedRacecourse,
            raceNumber,
            references.Count);

        return references;
    }
}