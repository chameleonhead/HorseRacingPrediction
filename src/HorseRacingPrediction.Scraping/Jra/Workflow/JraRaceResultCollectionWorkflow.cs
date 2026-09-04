using System.Linq;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// <see cref="IJraRaceResultCollectionWorkflow"/> の実装。
/// オーケストレーションのみを行い、HTML解析やページ遷移の詳細は
/// <see cref="JraSession.Navigate"/>（Navigator/Parser層）に委譲する。
///
/// <see cref="IDataCollectionWriteService.DeclareRaceEntryResultAsync"/> は
/// <see cref="RaceAggregate"/> が <c>ResultDeclared</c> 状態に遷移していないと
/// ドメイン層のガードで例外を送出し、API はそれを 409 (Conflict) として返す。
/// <see cref="HttpDataCollectionWriteService"/> はこの 409 を「既に記録済み」という
/// 成功扱いメッセージにマッピングしているため、<see cref="DeclareRaceResultAsync"/> を
/// 呼ばずに着順登録だけを行うと、実際にはデータが保存されないまま見かけ上成功する
/// （サイレントなデータ欠落）。そのため、着順登録の前に必ず1着馬の情報で
/// レース全体の確定宣言を行う。
///
/// 払戻（<see cref="IDataCollectionWriteService.DeclareRacePayoutsAsync"/>）は
/// 本フェーズでは実装しない（別タスクで対応する）。
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

        var winningEntry = resultPage.Results.FirstOrDefault(e => e.FinishPosition == 1);
        if (winningEntry is not null)
        {
            try
            {
                await _writeService.DeclareRaceResultAsync(
                    raceId: dataCollectionRaceId,
                    winningHorseName: winningEntry.HorseName,
                    declaredAt: null,
                    winningHorseId: null,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"レース確定宣言エラー: {ex.Message}");
            }
        }
        else
        {
            errors.Add("レース確定宣言エラー: 1着馬が結果に見つかりませんでした。");
        }

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
