// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Scraping.Workflow;

/// <summary>
/// JRA サイトから今後の開催日を収集するワークフロー。
/// URL 発見ではなく、<see cref="JraSiteDataCollector"/> のクリック遷移で取得する。
/// </summary>
public sealed class JraRaceScheduleCollectionWorkflow
{
    /// <summary>
    /// 基準日をもとに開催日一覧を収集し、先行日数で絞った予定日を返す。
    /// </summary>
    public async Task<JraRaceScheduleCollectionResult> CollectAsync(
        DateOnly referenceDate,
        int lookaheadDays,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var agent = await JraSiteDataCollector.CreateAsync(cancellationToken);
            var schedule = await agent.RequestRaceScheduleDatesAsync(referenceDate, cancellationToken);
            if (!schedule.Success || schedule.Data is null)
            {
                return new JraRaceScheduleCollectionResult(
                    referenceDate,
                    [],
                    [],
                    schedule.Error ?? "開催日一覧の収集に失敗しました。");
            }

            var raceDates = schedule.Data.RaceDates
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var endDate = referenceDate.AddDays(Math.Max(0, lookaheadDays));
            var upcoming = raceDates
                .Where(d => d >= referenceDate && d <= endDate)
                .ToList();

            return new JraRaceScheduleCollectionResult(
                referenceDate,
                raceDates,
                upcoming,
                null);
        }
        catch (Exception ex)
        {
            return new JraRaceScheduleCollectionResult(referenceDate, [], [], ex.Message);
        }
    }
}
#endif
