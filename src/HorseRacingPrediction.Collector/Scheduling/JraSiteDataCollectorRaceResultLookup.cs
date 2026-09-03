// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraSiteDataCollectorRaceResultLookup : IJraRaceResultLookup
{
    public async Task<JraExtractionEnvelope<JraRaceResultSummary>> GetRaceResultAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestRaceResultAsync(raceDate, racecourse, raceNumber, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraRaceResultSummary>> GetRaceResultByUrlAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        await taskAgent.NavigateAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
        var extraction = await taskAgent.ExtractCurrentPageAsync(cancellationToken).ConfigureAwait(false);
        return extraction.ToTyped<JraRaceResultSummary>();
    }
}
#endif
