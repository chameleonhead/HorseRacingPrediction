// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Scheduling;

public interface IJraProfileLookup
{
    Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(
        string horseName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 競走馬のプロフィールに加え、<see cref="JraHorseScraper"/> が競走馬情報ページから
    /// 抽出した過去の競走成績もあわせて取得する。
    /// </summary>
    Task<JraExtractionEnvelope<JraHorseProfileData>> GetHorseProfileWithHistoryAsync(
        string horseName,
        CancellationToken cancellationToken = default);

    Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(
        string jockeyName,
        CancellationToken cancellationToken = default);

    Task<JraExtractionEnvelope<JraEntityProfile>> GetTrainerProfileAsync(
        string trainerName,
        CancellationToken cancellationToken = default);
}
#endif
