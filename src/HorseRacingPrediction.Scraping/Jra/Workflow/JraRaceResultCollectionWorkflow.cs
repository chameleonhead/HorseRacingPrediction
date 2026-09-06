using System.Linq;
using System.Text.Json;
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
/// 天候・馬場状態・払戻（<see cref="IDataCollectionWriteService.DeclareRacePayoutsAsync"/>）は
/// 結果ページパーサーがページから抽出できた場合のみ記録する。実ページのHTML構造は
/// 未調査のため、抽出できなかった項目は記録をスキップし、着順登録自体は失敗させない。
///
/// <para>
/// 成績収集（本ワークフロー）は出馬表収集（<see cref="JraRaceCardCollectionWorkflow"/>）とは
/// 独立して過去日を遡って動作しうる（<c>ResultLookbackDays</c>）。一方、出馬表収集は
/// 前方参照のみ（<c>ScheduleLookaheadDays</c>）であるため、出馬表収集が一度も行われず
/// <see cref="RaceAggregate"/> がCreateすらされていない（さらに開催選択カードが
/// Publishされていない）レースの成績・天候・馬場状態を先に収集しようとするケースが
/// 実運用で確認された（"Race is not created." による500エラー）。そのため、
/// 結果ページから判明する範囲のメタデータ（レース名・出走頭数）で
/// <see cref="IDataCollectionWriteService.UpsertRaceAsync"/>（作成 or 更新、
/// 出走頭数指定時はカード公開まで行う冪等な操作）を必ず先に呼び、レースが
/// 存在すること・結果宣言可能な状態であることを保証してから、結果・天候・馬場状態・
/// 払戻をまとめて登録する。
/// </para>
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

        try
        {
            await _writeService.UpsertRaceAsync(
                raceDate: raceId.Date.ToString("yyyy-MM-dd"),
                racecourseCode: racecourseName,
                raceNumber: raceId.Number,
                raceName: string.IsNullOrWhiteSpace(resultPage.RaceName) ? $"{racecourseName}{raceId.Number}R" : resultPage.RaceName,
                entryCount: resultPage.Results.Count > 0 ? resultPage.Results.Count : null,
                gradeCode: null,
                surfaceCode: null,
                distanceMeters: null,
                directionCode: null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
        {
            // レース自体が作成・カード公開できていなければ、この後の結果・天候等の登録は
            // すべて同じ原因（"Race is not created." / "カード公開前"）で失敗するだけなので、
            // ここで打ち切って分かりやすい1件のエラーにまとめる。
            errors.Add($"レース登録エラー: {ex.Message}");
            return new RaceResultCollectionResult(raceId, dataCollectionRaceId, savedHorseNumbers, errors);
        }

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
            catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
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
            catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
            {
                errors.Add($"着順記録エラー: HorseNumber={entry.HorseNumber} — {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(resultPage.WeatherText))
        {
            try
            {
                await _writeService.RecordWeatherObservationAsync(
                    raceId: dataCollectionRaceId,
                    observationTime: DateTimeOffset.UtcNow,
                    weatherCode: null,
                    weatherText: resultPage.WeatherText,
                    temperatureCelsius: null,
                    humidityPercent: null,
                    windDirectionCode: null,
                    windSpeedMeterPerSecond: null,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
            {
                errors.Add($"天候記録エラー: {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(resultPage.TrackConditionText))
        {
            try
            {
                await _writeService.RecordTrackConditionObservationAsync(
                    raceId: dataCollectionRaceId,
                    observationTime: DateTimeOffset.UtcNow,
                    turfConditionCode: null,
                    dirtConditionCode: null,
                    goingDescriptionText: resultPage.TrackConditionText,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
            {
                errors.Add($"馬場状態記録エラー: {ex.Message}");
            }
        }

        if (resultPage.Payouts is not null && winningEntry is not null)
        {
            try
            {
                await _writeService.DeclareRacePayoutsAsync(
                    raceId: dataCollectionRaceId,
                    winPayoutsJson: ToPayoutJson(resultPage.Payouts.WinPayouts),
                    placePayoutsJson: ToPayoutJson(resultPage.Payouts.PlacePayouts),
                    quinellaPayoutsJson: ToPayoutJson(resultPage.Payouts.QuinellaPayouts),
                    exactaPayoutsJson: ToPayoutJson(resultPage.Payouts.ExactaPayouts),
                    trifectaPayoutsJson: ToPayoutJson(resultPage.Payouts.TrifectaPayouts),
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
            {
                errors.Add($"払戻記録エラー: {ex.Message}");
            }
        }

        return new RaceResultCollectionResult(raceId, dataCollectionRaceId, savedHorseNumbers, errors);
    }

    private static string? ToPayoutJson(IReadOnlyList<PayoutLine> payouts)
        => payouts.Count == 0
            ? null
            : JsonSerializer.Serialize(payouts.Select(p => new { combination = p.Combination, amount = p.Amount }));

    private static string? FormatTime(TimeSpan? time) =>
        time is null
            ? null
            : $"{(int)time.Value.TotalMinutes}:{time.Value.Seconds:D2}.{time.Value.Milliseconds / 100:D1}";
}
