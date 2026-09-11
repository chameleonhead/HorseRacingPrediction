using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

public sealed partial class JraRaceCardCollectionWorkflow
{
    public async Task<RaceCardRaceOutcome> RefreshAsync(RaceId raceId, string? targetRaceId, CancellationToken cancellationToken = default)
    {
        var page = await _session.Navigate.ToRaceCardAsync(raceId, cancellationToken);
        if (page is not JraRaceCardPage card) throw new JraCollectionException("出馬表を取得できませんでした。");
        return await RefreshPageAsync(card, targetRaceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RaceCardRaceOutcome> RefreshPageAsync(JraRaceCardPage card, string? targetRaceId,
        CancellationToken cancellationToken = default)
    {
        var raceId = card.RaceId;
        if (card.RaceId != raceId)
            throw new JraRaceIdentityMismatchException(JraPageKind.RaceCard, card.Url, raceId.ToString(), card.RaceId.ToString());
        if (string.IsNullOrWhiteSpace(card.RaceName) || card.Entries.Count == 0)
            throw new JraCollectionException("出馬表のレース名・出走情報を確認できませんでした。");
        var entries = card.Entries.Select(x => new RaceResultEntryBulkDto(x.HorseNumber, null, null, null,
            null, null, null, HorseName: x.HorseName, JockeyName: x.JockeyName, TrainerName: x.TrainerName,
            GateNumber: x.FrameNumber, AssignedWeight: x.AssignedWeight, BodyWeight: x.BodyWeight,
            BodyWeightChange: x.BodyWeightChange, OwnerName: x.OwnerName, SexCode: x.SexCode, Age: x.Age,
            BreederName: x.BreederName, SireName: x.SireName, DamName: x.DamName,
            DamsireName: x.DamsireName, CoatColor: x.CoatColor)).ToArray();
        var saved = await _writeService.DeclareRaceResultBulkAsync(new(raceId.Date,
            RaceCourseNames.GetJraName(raceId.Course), raceId.Number, card.RaceName, EntryCount: entries.Length,
            GradeCode: card.GradeCode, DistanceMeters: card.CourseSpec?.DistanceMeters,
            SurfaceCode: card.CourseSpec is null ? null : string.Join("→", card.CourseSpec.Surfaces.Select(x => x == CourseSurface.Turf ? "芝" : "ダート")),
            DirectionCode: card.CourseSpec?.Direction switch
            {
                CourseDirection.Left => "左",
                CourseDirection.Right => "右",
                CourseDirection.Straight => "直",
                _ => null
            },
            StartTime: card.StartTime, CourseLayout: card.CourseSpec?.RawLayout,
            Entries: entries, TargetRaceId: targetRaceId, RefreshExistingData: targetRaceId is not null, IsRaceCard: true), cancellationToken);
        var persistedRaceId = targetRaceId ?? saved.RaceId;
        await _writeService.RecordSourceCitationAsync([new CitationSubject("Race", persistedRaceId)],
            card.Url, "JRA出馬表", cancellationToken);
        return new(raceId.Number, persistedRaceId, card.RaceName, card.Url,
            saved.Errors.Count == 0 ? null : string.Join("; ", saved.Errors), card.Entries);
    }
}
