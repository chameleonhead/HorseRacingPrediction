// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraSiteDataCollectorProfileLookup : IJraProfileLookup
{
    public async Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(
        string horseName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestHorseProfileAsync(horseName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraHorseProfileData>> GetHorseProfileWithHistoryAsync(
        string horseName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestHorseProfileWithHistoryAsync(horseName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(
        string jockeyName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestJockeyProfileAsync(jockeyName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraEntityProfile>> GetTrainerProfileAsync(
        string trainerName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync(cancellationToken).ConfigureAwait(false);
        return await taskAgent.RequestTrainerProfileAsync(trainerName, cancellationToken).ConfigureAwait(false);
    }
}
#endif
