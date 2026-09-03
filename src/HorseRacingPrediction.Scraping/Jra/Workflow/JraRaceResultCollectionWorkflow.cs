using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// <see cref="IJraRaceResultCollectionWorkflow"/> の実装。
/// オーケストレーションのみを行い、HTML解析やページ遷移の詳細は
/// <see cref="JraSession.Navigate"/>（Navigator/Parser層）に委譲する。
///
/// 払戻（<see cref="IDataCollectionWriteService.DeclareRacePayoutsAsync"/>）および
/// レース全体の確定宣言（<see cref="IDataCollectionWriteService.DeclareRaceResultAsync"/>）は
/// 本フェーズでは実装しない。新基盤（Collector統合）移行と着順登録を同時に行うと
/// 問題の切り分けが難しくなるため、別タスクで対応する。
/// </summary>
public sealed class JraRaceResultCollectionWorkflow
    : IJraRaceResultCollectionWorkflow
{
    private readonly JraSession _session;
    private readonly IDataCollectionWriteService _writeService;

    public JraRaceResultCollectionWorkflow(
        JraSession session,
        IDataCollectionWriteService writeService)
    {
        _session = session;
        _writeService = writeService;
    }

    public async Task<RaceResultCollectionResult> CollectAsync(
        RaceId raceId,
        CancellationToken cancellationToken = default)
    {
        if (raceId.Course == RaceCourse.Unknown)
        {
            throw new ArgumentException(
                $"RaceCourse.Unknown は永続化 ID の生成に使用できません。Date={raceId.Date:yyyy-MM-dd}",
                nameof(raceId));
        }

        var resultPageResult = await _session.Navigate.ToRaceResultAsync(raceId, cancellationToken);

        if (resultPageResult is not JraRaceResultPage resultPage)
        {
            throw new JraCollectionException(
                $"レース結果ページを取得できませんでした。 Kind={resultPageResult.Kind}, Url={resultPageResult.Url}");
        }

        var racecourseName = RaceCourseNames.GetJraName(raceId.Course);
        var dataCollectionRaceId = DeterministicIdGenerator.BuildRaceId(
            raceId.Date, racecourseName, raceId.Number);

        var savedHorseNumbers = new List<int>();
        var errors = new List<string>();

        foreach (var entry in resultPage.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _writeService.DeclareRaceEntryResultAsync(
                    raceId: dataCollectionRaceId,
                    horseNumber: entry.HorseNumber,
                    finishPosition: entry.FinishPosition,
                    officialTime: FormatTime(entry.Time),
                    marginText: null,
                    lastThreeFurlongTime: null,
                    abnormalResultCode: null,
                    prizeMoney: null,
                    cancellationToken: cancellationToken);

                savedHorseNumbers.Add(entry.HorseNumber);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"着順記録エラー: HorseNumber={entry.HorseNumber} — {ex.Message}");
            }
        }

        return new RaceResultCollectionResult(raceId, dataCollectionRaceId, savedHorseNumbers, errors);
    }

    private static string? FormatTime(TimeSpan? time) =>
        time is null
            ? null
            : $"{(int)time.Value.TotalMinutes}:{time.Value.Seconds:D2}.{time.Value.Milliseconds / 100:D1}";
}
