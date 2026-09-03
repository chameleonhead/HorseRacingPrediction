// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record ResultDayCollectionRequestPayload(
    DateOnly RaceDate,
    string ProviderType,
    IReadOnlyList<JraRaceResultUrl> Urls,
    int ExpectedRaceCount);
#endif