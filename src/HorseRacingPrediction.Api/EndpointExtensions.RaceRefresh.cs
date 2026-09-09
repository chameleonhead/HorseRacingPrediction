using EventFlow;
using EventFlow.Queries;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Shared = HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api;

public static partial class EndpointExtensions
{
    private static async Task<IResult> RefreshCollectedRaceAsync(Shared.DeclareRaceResultBulkRequest request,
        ICommandBus commands, IQueryProcessor queries, IDbContextProvider<EventStoreDbContext> dbProvider, CancellationToken token)
    {
        var id = request.TargetRaceId;
        if (string.IsNullOrWhiteSpace(id)) return Results.BadRequest(new[] { "対象レースが指定されていません。" });
        var existing = await queries.ProcessAsync(new ReadModelByIdQuery<RacePredictionContextReadModel>(id), token);
        if (existing is null || string.IsNullOrWhiteSpace(existing.RaceId)) return Results.NotFound();
        if (CollectionController.RaceReacquisitionEndpointExtensions.ResolveCourse(request.RacecourseCode) is null
            || existing.RaceDate != request.RaceDate || existing.RaceNumber != request.RaceNumber
            || CollectionController.RaceReacquisitionEndpointExtensions.ResolveCourse(existing.RacecourseCode)
                != CollectionController.RaceReacquisitionEndpointExtensions.ResolveCourse(request.RacecourseCode))
            return Results.Conflict(new[] { "取得ページが対象レースと一致しません。" });

        var entries = new List<EntryDetails>();
        var results = new List<EntryResultDetails>();
        var originHorse = request.SourceHorseId is null ? null
            : await queries.ProcessAsync(new ReadModelByIdQuery<HorseReadModel>(request.SourceHorseId), token);
        if (request.SourceHorseId is not null && (originHorse is null || !(request.Entries ?? []).Any(x => NormalizeDisplayName(x.HorseName ?? "") == NormalizeDisplayName(originHorse.RegisteredName))))
            return Results.Conflict(new[] { "取得元の馬がレースの出走馬に含まれていません。" });
        foreach (var source in request.Entries ?? [])
        {
            if (source.HorseNumber <= 0 || string.IsNullOrWhiteSpace(source.HorseName))
                return Results.BadRequest(new[] { "出走馬の識別情報が不足しています。" });
            var old = existing.Entries.FirstOrDefault(x => x.HorseNumber == source.HorseNumber);
            var entryId = old?.EntryId ?? DeterministicIdGenerator.BuildRaceEntryId(id, source.HorseNumber);
            static string? SubjectId(string kind, string? name) => string.IsNullOrWhiteSpace(name) ? null
                : DeterministicIdGenerator.BuildEntityId(kind, DeterministicIdGenerator.NormalizeKey(name));
            var horseId = SubjectId("horse", source.HorseName)!;
            var jockeyId = SubjectId("jockey", source.JockeyName);
            var trainerId = SubjectId("trainer", source.TrainerName);
            // 既存の出走登録では手入力IDや別の正規化規則も使われる。同じ名前なら関連IDを維持する。
            if (old is not null)
            {
                var horse = await queries.ProcessAsync(new ReadModelByIdQuery<HorseReadModel>(old.HorseId), token);
                if (horse is not null && NormalizeDisplayName(horse.RegisteredName) == NormalizeDisplayName(source.HorseName)) horseId = old.HorseId;
                if (old.JockeyId is not null && source.JockeyName is not null)
                {
                    var jockey = await queries.ProcessAsync(new ReadModelByIdQuery<JockeyReadModel>(old.JockeyId), token);
                    if (jockey is not null && NormalizeDisplayName(jockey.DisplayName) == NormalizeDisplayName(source.JockeyName)) jockeyId = old.JockeyId;
                }
                if (old.TrainerId is not null && source.TrainerName is not null)
                {
                    var trainer = await queries.ProcessAsync(new ReadModelByIdQuery<TrainerReadModel>(old.TrainerId), token);
                    if (trainer is not null && NormalizeDisplayName(trainer.DisplayName) == NormalizeDisplayName(source.TrainerName)) trainerId = old.TrainerId;
                }
            }
            if (originHorse is not null && NormalizeDisplayName(source.HorseName) == NormalizeDisplayName(originHorse.RegisteredName)) horseId = originHorse.HorseId;
            var registration = new RegisterEntryRequest(horseId, source.HorseNumber, jockeyId, trainerId,
                source.GateNumber, source.AssignedWeight, source.SexCode, source.Age, source.BodyWeight, source.BodyWeightChange,
                EntryId: entryId, HorseName: source.HorseName, JockeyName: source.JockeyName,
                TrainerName: source.TrainerName, OwnerName: source.OwnerName);
            await EnsureRelatedSubjectsAsync(registration, commands, dbProvider, token);
            // 馬主・生産者・血統は出馬表由来の場合だけ馬プロフィールへ反映する。
            // レース結果には馬主を特定できる情報がないため、結果再取得からは更新しない。
            if (request.IsRaceCard && new[] { source.OwnerName, source.BreederName, source.SireName, source.DamName, source.DamsireName, source.CoatColor }
                    .Any(value => !string.IsNullOrWhiteSpace(value)))
                await commands.PublishAsync(new UpdateHorseProfileCommand(
                    new HorseRacingPrediction.Domain.Horses.HorseId(horseId),
                    ownerName: source.OwnerName, breederName: source.BreederName,
                    sireName: source.SireName, damName: source.DamName,
                    damsireName: source.DamsireName, coatColor: source.CoatColor), token);
            entries.Add(new(entryId, horseId, source.HorseNumber, jockeyId, trainerId,
                source.GateNumber, source.AssignedWeight, source.SexCode, source.Age, source.BodyWeight,
                source.BodyWeightChange, null, source.OwnerName));
            results.Add(new(entryId, source.FinishPosition, source.OfficialTime, source.MarginText,
                source.LastThreeFurlongTime, source.AbnormalResultCode, source.PrizeMoney, source.CornerPositions,
                source.Popularity, source.OriginalFinishPosition, source.IsDeadHeat, source.Average1F));
        }
        static IReadOnlyList<PayoutEntry> Payouts(IReadOnlyList<Shared.PayoutEntryDto>? values) =>
            values?.Select(x => new PayoutEntry(x.Combination, x.Amount)).ToArray() ?? [];
        var p = request.Payouts;
        var w = request.Weather;
        var t = request.TrackCondition;
        var winner = request.Entries?.FirstOrDefault(x => x.FinishPosition == 1);
        var winningId = winner is null ? null : entries.FirstOrDefault(x => x.HorseNumber == winner.HorseNumber)?.HorseId;
        var data = new CollectedRaceData(request.RaceName, request.GradeCode, request.SurfaceCode,
            request.DistanceMeters, request.DirectionCode, request.EntryCount, entries,
            request.IsRaceCard ? null : results, request.WinningHorseName ?? winner?.HorseName, winningId,
            p is null ? null : new(p.DeclaredAt, Payouts(p.WinPayouts), Payouts(p.PlacePayouts),
                Payouts(p.QuinellaPayouts), Payouts(p.ExactaPayouts), Payouts(p.TrifectaPayouts),
                Payouts(p.BracketQuinellaPayouts), Payouts(p.WidePayouts), Payouts(p.TrioPayouts)),
            w is null ? null : new(w.ObservationTime, w.WeatherCode, w.WeatherText, w.TemperatureCelsius,
                w.HumidityPercent, w.WindDirectionCode, w.WindSpeedMeterPerSecond),
            t is null ? null : new(t.ObservationTime, t.TurfConditionCode, t.DirtConditionCode, t.GoingDescriptionText),
            request.StartTime, request.OverallPaceText, request.CornerPassagesText, request.CourseLayout);
        var outcome = await commands.PublishAsync(new RefreshCollectedRaceCommand(new RaceId(id), data), token);
        return outcome.IsSuccess ? Results.Ok(new Shared.DeclareRaceResultBulkResponse(id, []))
            : Results.BadRequest(new[] { "再取得情報の保存に失敗しました。" });
    }
}
