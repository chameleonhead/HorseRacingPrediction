namespace HorseRacingPrediction.Domain.Races;

public partial class RaceAggregate
{
    public void RefreshCollectedData(CollectedRaceData data)
    {
        if (!_state.IsCreated) throw new InvalidOperationException("Race is not created.");
        if (string.IsNullOrWhiteSpace(data.RaceName)) throw new InvalidOperationException("Race name is required.");
        var metadataChanged = (data.GradeCode is not null && data.GradeCode != _state.GradeCode)
            || (data.SurfaceCode is not null && data.SurfaceCode != _state.SurfaceCode)
            || (data.DistanceMeters is not null && data.DistanceMeters != _state.DistanceMeters)
            || (data.DirectionCode is not null && data.DirectionCode != _state.DirectionCode);
        Emit(new RaceDataCorrected(data.RaceName, gradeCode: data.GradeCode,
            surfaceCode: data.SurfaceCode, distanceMeters: data.DistanceMeters, directionCode: data.DirectionCode,
            reason: "JRA再取得", entryCount: data.EntryCount, startTime: data.StartTime, overallPaceText: data.OverallPaceText,
            cornerPassagesText: data.CornerPassagesText, courseLayout: data.CourseLayout));
        if (_state.Status == RaceStatus.Draft && data.EntryCount is > 0) PublishCard(data.EntryCount.Value);

        foreach (var incoming in data.Entries)
        {
            var old = _state.Entries.FirstOrDefault(x => x.EntryId == incoming.EntryId);
            var entry = incoming with
            {
                JockeyId = incoming.JockeyId ?? old?.JockeyId,
                TrainerId = incoming.TrainerId ?? old?.TrainerId,
                GateNumber = incoming.GateNumber ?? old?.GateNumber,
                AssignedWeight = incoming.AssignedWeight ?? old?.AssignedWeight,
                SexCode = incoming.SexCode ?? old?.SexCode, Age = incoming.Age ?? old?.Age,
                DeclaredWeight = incoming.DeclaredWeight ?? old?.DeclaredWeight,
                DeclaredWeightDiff = incoming.DeclaredWeightDiff ?? old?.DeclaredWeightDiff,
                RunningStyleCode = incoming.RunningStyleCode ?? old?.RunningStyleCode,
                OwnerName = incoming.OwnerName ?? old?.OwnerName
            };
            if (entry == old && !metadataChanged) continue;
            RegisterEntry(entry.EntryId, entry.HorseId, entry.HorseNumber, entry.JockeyId, entry.TrainerId,
                entry.GateNumber, entry.AssignedWeight, entry.SexCode, entry.Age,
                entry.DeclaredWeight, entry.DeclaredWeightDiff, entry.RunningStyleCode, entry.OwnerName);
        }

        if (data.WinningHorseName is not null && (_state.WinningHorseName != data.WinningHorseName
            || _state.WinningHorseId != data.WinningHorseId || _state.Status < RaceStatus.ResultDeclared))
            Emit(new RaceResultDeclared(data.WinningHorseName, _state.ResultDeclaredAt ?? DateTimeOffset.UtcNow,
                data.WinningHorseId, _state.StewardReportText));

        if (data.Results is not null)
        {
            if (_state.Status < RaceStatus.ResultDeclared) throw new InvalidOperationException("Result has not been published.");
            foreach (var incoming in data.Results)
            {
                var old = _state.EntryResults.LastOrDefault(x => x.EntryId == incoming.EntryId);
                var result = incoming with
                {
                    // 異常区分がある場合の着順・時計のnullは、取消等への変更を表す。
                    FinishPosition = incoming.FinishPosition ?? (incoming.AbnormalResultCode is null ? old?.FinishPosition : null),
                    OfficialTime = incoming.OfficialTime ?? (incoming.AbnormalResultCode is null ? old?.OfficialTime : null),
                    MarginText = incoming.MarginText ?? old?.MarginText,
                    LastThreeFurlongTime = incoming.LastThreeFurlongTime ?? old?.LastThreeFurlongTime,
                    PrizeMoney = incoming.PrizeMoney ?? old?.PrizeMoney,
                    CornerPositions = incoming.CornerPositions ?? old?.CornerPositions,
                    Popularity = incoming.Popularity ?? old?.Popularity,
                    OriginalFinishPosition = incoming.OriginalFinishPosition ?? old?.OriginalFinishPosition,
                    Average1F = incoming.Average1F ?? old?.Average1F
                };
                var entry = _state.Entries.Single(x => x.EntryId == result.EntryId);
                // 履歴への再投影も必要なため、再取得した結果は自己完結した識別情報を含める。
                Emit(new EntryResultDeclared(result.EntryId, result.FinishPosition, result.OfficialTime,
                    result.MarginText, result.LastThreeFurlongTime, result.AbnormalResultCode, result.PrizeMoney,
                    result.CornerPositions, result.Popularity, result.OriginalFinishPosition, result.IsDeadHeat, result.Average1F, entry.HorseId, entry.JockeyId));
            }
        }

        if (data.Payouts is { } payout)
        {
            if (_state.Status < RaceStatus.ResultDeclared) throw new InvalidOperationException("Result has not been published.");
            var old = _state.PayoutResult;
            Emit(new PayoutResultDeclared(payout.DeclaredAt,
                Merge(payout.WinPayouts, old?.WinPayouts), Merge(payout.PlacePayouts, old?.PlacePayouts),
                Merge(payout.QuinellaPayouts, old?.QuinellaPayouts), Merge(payout.ExactaPayouts, old?.ExactaPayouts),
                Merge(payout.TrifectaPayouts, old?.TrifectaPayouts), Merge(payout.BracketQuinellaPayouts, old?.BracketQuinellaPayouts),
                Merge(payout.WidePayouts, old?.WidePayouts), Merge(payout.TrioPayouts, old?.TrioPayouts)));
        }
        if (data.Weather is { } weather)
            RecordWeatherObservation(weather.ObservationTime, weather.WeatherCode, weather.WeatherText,
                weather.TemperatureCelsius, weather.HumidityPercent, weather.WindDirectionCode, weather.WindSpeedMeterPerSecond);
        if (data.TrackCondition is { } track)
            RecordTrackConditionObservation(track.ObservationTime, track.TurfConditionCode, track.DirtConditionCode, track.GoingDescriptionText);
    }

    private static IReadOnlyList<PayoutEntry> Merge(IReadOnlyList<PayoutEntry>? incoming, IReadOnlyList<PayoutEntry>? old)
        => incoming is { Count: > 0 } ? incoming : old ?? [];
}
