using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// <see cref="IJraRaceCardCollectionWorkflow"/> の実装。
/// オーケストレーションのみを行い、HTML解析やページ遷移の詳細は
/// <see cref="JraSession.Navigate"/>（Navigator/Parser層）に委譲する。
/// </summary>
public sealed class JraRaceCardCollectionWorkflow
    : IJraRaceCardCollectionWorkflow
{
    private readonly JraSession _session;
    private readonly IDataCollectionWriteService _writeService;

    public JraRaceCardCollectionWorkflow(
        JraSession session,
        IDataCollectionWriteService writeService)
    {
        _session = session;
        _writeService = writeService;
    }

    public async Task<RaceCardCollectionResult> CollectAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default)
    {
        if (course == RaceCourse.Unknown)
        {
            throw new ArgumentException(
                $"RaceCourse.Unknown は永続化 ID の生成に使用できません。Date={date:yyyy-MM-dd}",
                nameof(course));
        }

        JraRaceListPage raceList;
        try
        {
            var listPage = await _session.Navigate.ToRaceListAsync(date, course, cancellationToken);

            if (listPage is not JraRaceListPage typedRaceList)
            {
                throw new JraCollectionException(
                    $"レース一覧ページを取得できませんでした。 Kind={listPage.Kind}, Url={listPage.Url}");
            }

            raceList = typedRaceList;
        }
        catch (JraNavigationException ex)
            when (ex.Reason == JraNavigationFailureReason.NotYetPublished)
        {
            // 開催予定はあるが出馬表がまだ公開されていない（＝正常な業務状態）。
            // ここでエラーとして記録すると呼び出し元のジョブが失敗扱いになり
            // 再試行の仕組みが働かないため、レースなし・エラーなしの結果を返し、
            // Collector側の「出馬表未公開のため再試行する」判定に委ねる。
            return new RaceCardCollectionResult(date, course, [], [], []);
        }
        catch (JraNavigationException ex)
        {
            // 例：過去月をまたいで開催選択ページの表示範囲外になったケース
            // （OutOfDisplayedRange）。出馬表は公開期間を過ぎると通常ページ自体が
            // 消えるため、これは恒久的な失敗として扱い、この競馬場・日付分の失敗として
            // 記録するに留め、呼び出し元（他の競馬場・他の日付のジョブ）の処理を止めない。
            // なお、レース一覧ページの種別不一致など「ナビゲーション以外」の失敗
            // （<see cref="JraCollectionException"/>）はここでは捕捉せず、そのまま
            // 呼び出し元へ伝播させる（実装上の不整合として扱うべきため）。
            var message = $"レース一覧取得エラー: Date={date:yyyy-MM-dd} Course={course} — {ex.Message}";
            return new RaceCardCollectionResult(date, course, [], [message], []);
        }

        var raceIds = new List<string>();
        var errors = new List<string>();
        var outcomes = new List<RaceCardRaceOutcome>();
        var racecourseName = RaceCourseNames.GetJraName(course);

        foreach (var race in raceList.Races)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var (raceId, raceName, sourceUrl) = await CollectAndSaveRaceAsync(race, date, racecourseName, cancellationToken);
                raceIds.Add(raceId);
                outcomes.Add(new RaceCardRaceOutcome(race.Number, raceId, raceName, sourceUrl, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
            {
                var message = $"レース収集エラー: RaceNumber={race.Number} — {ex.Message}";
                errors.Add(message);
                outcomes.Add(new RaceCardRaceOutcome(race.Number, null, race.Name, null, message));
            }
        }

        return new RaceCardCollectionResult(date, course, raceIds, errors, outcomes);
    }

    private async Task<(string RaceId, string RaceName, string SourceUrl)> CollectAndSaveRaceAsync(
        RaceSummary race,
        DateOnly date,
        string racecourseName,
        CancellationToken cancellationToken)
    {
        var cardPageResult = await _session.Navigate.ToRaceCardAsync(race.Id, cancellationToken);

        if (cardPageResult is not JraRaceCardPage card)
        {
            throw new JraCollectionException(
                $"出馬表ページを取得できませんでした。 Kind={cardPageResult.Kind}, Url={cardPageResult.Url}");
        }

        var raceName = string.IsNullOrWhiteSpace(card.RaceName)
            ? race.Name ?? $"R{race.Number}"
            : card.RaceName;

        var raceId = await _writeService.UpsertRaceAsync(
            raceDate: date.ToString("yyyy-MM-dd"),
            racecourseCode: racecourseName,
            raceNumber: race.Number,
            raceName: raceName,
            entryCount: card.Entries.Count > 0 ? card.Entries.Count : null,
            gradeCode: null,
            surfaceCode: null,
            distanceMeters: null,
            directionCode: null,
            cancellationToken: cancellationToken);

        foreach (var entry in card.Entries)
        {
            // 実サイト確認（2026-09-06）で判明: 馬主名は別ページへの遷移なしに出馬表の
            // 馬名セルから直接取得できる（RaceCardPageParser参照）。取得できた場合のみ
            // 馬主付きで馬を登録する（失敗しても出走登録自体は継続する）。
            if (!string.IsNullOrWhiteSpace(entry.OwnerName))
            {
                try
                {
                    await _writeService.UpsertHorseWithOwnerAsync(
                        registeredName: entry.HorseName,
                        normalizedName: null,
                        sexCode: null,
                        birthDate: null,
                        ownerName: entry.OwnerName,
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !ApiFailureClassifier.IsFatalServerError(ex))
                {
                    // 馬主登録の失敗で出走登録自体は失敗させない。
                }
            }

            await _writeService.UpsertRaceEntryAsync(
                raceId: raceId,
                horseNumber: entry.HorseNumber,
                horseName: entry.HorseName,
                jockeyName: entry.JockeyName,
                trainerName: entry.TrainerName,
                gateNumber: entry.FrameNumber,
                assignedWeight: entry.AssignedWeight,
                sexCode: null,
                age: null,
                declaredWeight: null,
                declaredWeightDiff: null,
                cancellationToken: cancellationToken);
        }

        return (raceId, raceName, card.Url);
    }
}
