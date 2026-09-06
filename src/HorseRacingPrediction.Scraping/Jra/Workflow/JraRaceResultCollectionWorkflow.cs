using System.Linq;
using System.Text.RegularExpressions;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// <see cref="IJraRaceResultCollectionWorkflow"/> の実装。
/// オーケストレーションのみを行い、HTML解析やページ遷移の詳細は
/// <see cref="JraSession.Navigate"/>（Navigator/Parser層）に委譲する。
///
/// <para>
/// レース1件分の登録（レース作成/更新・結果宣言・全馬の着順・天候・馬場状態・払戻）は、
/// 従来は<see cref="IDataCollectionWriteService"/>の個別メソッドを6種類以上・
/// 馬の頭数だけ呼び分けており、レース1件あたり10〜20回超のHTTPラウンドトリップが
/// 発生していた。<see cref="IDataCollectionWriteService.DeclareRaceResultBulkAsync"/>
/// により、これらを1回のAPI呼び出しにまとめる。
/// </para>
///
/// <see cref="RaceAggregate"/> は<c>ResultDeclared</c>状態に遷移していないと
/// 各馬の着順登録をドメイン層のガードで拒否するため、一括登録リクエストの中でも
/// 必ず1着馬の情報でレース全体の確定宣言（結果宣言）を先に行ってから各馬の着順を
/// 登録する（API側の一括登録エンドポイント実装で保証）。
///
/// 天候・馬場状態・払戻は結果ページパーサーがページから抽出できた場合のみ記録する。
///
/// <para>
/// 成績収集（本ワークフロー）は出馬表収集（<see cref="JraRaceCardCollectionWorkflow"/>）とは
/// 独立して過去日を遡って動作しうる（<c>ResultLookbackDays</c>）。一方、出馬表収集は
/// 前方参照のみ（<c>ScheduleLookaheadDays</c>）であるため、出馬表収集が一度も行われず
/// <see cref="RaceAggregate"/> がCreateすらされていない（さらに開催選択カードが
/// Publishされていない）レースの成績・天候・馬場状態を先に収集しようとするケースが
/// 実運用で確認された（"Race is not created." による500エラー）。そのため、
/// 結果ページから判明する範囲のメタデータ（レース名・出走頭数）でのレース作成/更新
/// （出走頭数指定時はカード公開まで行う冪等な操作）を、一括登録リクエストの中で
/// 必ず最初に行う。
/// </para>
/// </summary>
public sealed class JraRaceResultCollectionWorkflow
    : IJraRaceResultCollectionWorkflow
{
    private static readonly Regex FailedHorseNumberPattern = new(@"HorseNumber=(\d+)", RegexOptions.Compiled);

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

        // 依頼書8節: Navigationで要求したRaceIdと、ページ自身から解析したRaceIdを
        // 必ず照合する。HTMLとして正常なページが取得できていても、別レースであれば
        // 成功扱いにしない。
        if (resultPage.RaceId != raceId)
        {
            throw new JraRaceIdentityMismatchException(
                JraPageKind.RaceResult,
                resultPage.Url,
                raceId.ToString(),
                resultPage.RaceId.ToString());
        }

        var racecourseName = RaceCourseNames.GetJraName(raceId.Course);
        var dataCollectionRaceId = DeterministicIdGenerator.BuildRaceId(
            raceId.Date, racecourseName, raceId.Number);

        // 引用元（取得元URL）は、後段の登録が部分的に失敗した場合でも「このURLから
        // 取得を試みた」という事実自体に調査上の価値があるため、結果ページの取得に
        // 成功した時点で記録する（登録の成否とは独立）。同一レースを複数回取得しても
        // メモとして複数件蓄積されるだけで、既存の引用元を上書きしない。
        await _writeService.RecordSourceCitationAsync(
            [new CitationSubject("Race", dataCollectionRaceId)],
            resultPage.Url,
            "JRAレース結果",
            cancellationToken);

        var errors = new List<string>();

        // 実運用で、結果ページのパース失敗時に馬番=0・馬名=空のプレースホルダー値の
        // まま登録が続行され、DB上に「1着〜7着すべて馬番0・馬名なし」という
        // 見かけ上は成功しているが実際は無意味なデータが記録される事象が確認された。
        // 想定した情報（馬番・馬名）を取得できていないエントリーは、ここで検知して
        // 送信対象から除外し、エラーとして記録する（サイレントな欠損データ登録を防ぐ）。
        var validResults = new List<RaceResultEntry>();
        foreach (var entry in resultPage.Results)
        {
            if (entry.HorseNumber <= 0 || string.IsNullOrWhiteSpace(entry.HorseName))
            {
                errors.Add(
                    $"着順記録エラー: 想定した馬番/馬名を取得できませんでした（パース失敗の可能性）。HorseNumber={entry.HorseNumber} HorseName='{entry.HorseName}' FinishPosition={entry.FinishPosition}");
                continue;
            }

            validResults.Add(entry);
        }

        if (resultPage.Results.Count > 0 && validResults.Count == 0)
        {
            // 全エントリーが不正（ページ全体のパース失敗）の場合、送信しても
            // 意味のあるデータは何も登録できないため、API呼び出し自体を行わない。
            return new RaceResultCollectionResult(raceId, dataCollectionRaceId, [], errors, resultPage.Url);
        }

        var winningEntry = validResults.FirstOrDefault(e => e.FinishPosition == 1);
        if (winningEntry is null)
        {
            errors.Add("レース確定宣言エラー: 1着馬が結果に見つかりませんでした。");
        }

        var entries = validResults
            .Select(entry => new RaceResultBulkEntry(
                entry.HorseNumber,
                entry.FinishPosition,
                FormatTime(entry.Time),
                MarginText: entry.MarginRaw,
                LastThreeFurlongTime: null,
                AbnormalResultCode: ToAbnormalResultCode(entry.ResultStatus),
                PrizeMoney: null))
            .ToList();

        var weather = string.IsNullOrWhiteSpace(resultPage.WeatherText)
            ? null
            : new RaceResultBulkWeather(
                DateTimeOffset.UtcNow, WeatherCode: null, resultPage.WeatherText,
                TemperatureCelsius: null, HumidityPercent: null, WindDirectionCode: null, WindSpeedMeterPerSecond: null);

        var trackCondition = string.IsNullOrWhiteSpace(resultPage.TrackConditionText)
            ? null
            : new RaceResultBulkTrackCondition(
                DateTimeOffset.UtcNow, TurfConditionCode: null, DirtConditionCode: null, resultPage.TrackConditionText);

        var payouts = resultPage.Payouts is null || winningEntry is null
            ? null
            : new RaceResultBulkPayouts(
                DateTimeOffset.UtcNow,
                ToPayoutEntries(resultPage.Payouts.WinPayouts),
                ToPayoutEntries(resultPage.Payouts.PlacePayouts),
                ToPayoutEntries(resultPage.Payouts.QuinellaPayouts),
                ToPayoutEntries(resultPage.Payouts.ExactaPayouts),
                ToPayoutEntries(resultPage.Payouts.TrifectaPayouts));

        var request = new RaceResultBulkRequest(
            RaceDate: raceId.Date.ToString("yyyy-MM-dd"),
            RacecourseCode: racecourseName,
            RaceNumber: raceId.Number,
            RaceName: string.IsNullOrWhiteSpace(resultPage.RaceName) ? $"{racecourseName}{raceId.Number}R" : resultPage.RaceName,
            EntryCount: resultPage.Results.Count > 0 ? resultPage.Results.Count : null,
            GradeCode: null,
            SurfaceCode: null,
            DistanceMeters: null,
            DirectionCode: null,
            WinningHorseName: winningEntry?.HorseName,
            DeclaredAt: null,
            Entries: entries,
            Weather: weather,
            TrackCondition: trackCondition,
            Payouts: payouts);

        RaceResultBulkOutcome outcome;
        try
        {
            outcome = await _writeService.DeclareRaceResultBulkAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
        {
            // レース自体が作成・カード公開できていなければ、結果・天候等の登録は
            // すべて同じ原因（"Race is not created." / "カード公開前"）で失敗するだけなので、
            // ここで打ち切って分かりやすい1件のエラーにまとめる。
            errors.Add($"レース登録エラー: {ex.Message}");
            return new RaceResultCollectionResult(raceId, dataCollectionRaceId, [], errors, resultPage.Url);
        }

        errors.AddRange(outcome.Errors);

        // レース自体の作成/更新がAPI側で失敗した場合、それ以降の項目はAPI側でも
        // 処理されていないため、保存済み馬番は空のまま返す。
        if (outcome.Errors.Any(e => e.StartsWith("レース登録エラー", StringComparison.Ordinal)))
        {
            return new RaceResultCollectionResult(raceId, dataCollectionRaceId, [], errors, resultPage.Url);
        }

        var failedHorseNumbers = outcome.Errors
            .Select(e => FailedHorseNumberPattern.Match(e))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();

        var savedHorseNumbers = entries
            .Where(e => e.HorseNumber > 0 && !failedHorseNumbers.Contains(e.HorseNumber))
            .Select(e => e.HorseNumber)
            .ToList();

        return new RaceResultCollectionResult(raceId, dataCollectionRaceId, savedHorseNumbers, errors, resultPage.Url);
    }

    private static IReadOnlyList<RaceResultBulkPayoutEntry>? ToPayoutEntries(IReadOnlyList<PayoutLine> payouts)
        => payouts.Count == 0
            ? null
            : payouts.Select(p => new RaceResultBulkPayoutEntry(p.Combination, p.Amount)).ToList();

    private static string? ToAbnormalResultCode(ResultStatus status) =>
        status switch
        {
            ResultStatus.Finished => null,
            ResultStatus.Cancelled => "取消",
            ResultStatus.Excluded => "除外",
            ResultStatus.DidNotFinish => "中止",
            ResultStatus.Disqualified => "失格",
            _ => null,
        };

    private static string? FormatTime(TimeSpan? time) =>
        time is null
            ? null
            : $"{(int)time.Value.TotalMinutes}:{time.Value.Seconds:D2}.{time.Value.Milliseconds / 100:D1}";
}
