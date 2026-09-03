using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Models;
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

        var listPage = await _session.Navigate.ToRaceListAsync(date, course, cancellationToken);

        if (listPage is not JraRaceListPage raceList)
        {
            throw new JraCollectionException(
                $"レース一覧ページを取得できませんでした。 Kind={listPage.Kind}, Url={listPage.Url}");
        }

        var raceIds = new List<string>();
        var errors = new List<string>();
        var racecourseName = RaceCourseNames.GetJraName(course);

        foreach (var race in raceList.Races)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var raceId = await CollectAndSaveRaceAsync(race, date, racecourseName, cancellationToken);
                raceIds.Add(raceId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"レース収集エラー: RaceNumber={race.Number} — {ex.Message}");
            }
        }

        return new RaceCardCollectionResult(date, course, raceIds, errors);
    }

    private async Task<string> CollectAndSaveRaceAsync(
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
            await _writeService.UpsertRaceEntryAsync(
                raceId: raceId,
                horseNumber: entry.HorseNumber,
                horseName: entry.HorseName,
                jockeyName: entry.JockeyName,
                trainerName: null,
                gateNumber: entry.FrameNumber,
                assignedWeight: entry.AssignedWeight,
                sexCode: null,
                age: null,
                declaredWeight: null,
                declaredWeightDiff: null,
                cancellationToken: cancellationToken);
        }

        return raceId;
    }
}
