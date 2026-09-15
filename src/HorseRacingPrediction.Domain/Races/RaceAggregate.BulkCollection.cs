namespace HorseRacingPrediction.Domain.Races;

public partial class RaceAggregate
{
    /// <summary>
    /// Applies one collected race-result envelope. Validation is completed before the first event is emitted,
    /// allowing EventFlow to persist the resulting race event set in one aggregate commit.
    /// </summary>
    public void ApplyBulkRaceResult(BulkRaceResultData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateBulkRaceResult(data);

        if (!_state.IsCreated)
        {
            Create(data.RaceDate, data.RacecourseCode, data.RaceNumber, data.RaceName,
                gradeCode: data.GradeCode, surfaceCode: data.SurfaceCode,
                distanceMeters: data.DistanceMeters, directionCode: data.DirectionCode);
        }
        else if (_state.RaceName != data.RaceName
                 || _state.RacecourseCode != data.RacecourseCode
                 || _state.RaceNumber != data.RaceNumber
                 || (data.GradeCode is not null && _state.GradeCode != data.GradeCode)
                 || (data.SurfaceCode is not null && _state.SurfaceCode != data.SurfaceCode)
                 || (data.DistanceMeters is not null && _state.DistanceMeters != data.DistanceMeters)
                 || (data.DirectionCode is not null && _state.DirectionCode != data.DirectionCode))
        {
            CorrectRaceData(data.RaceName, data.RacecourseCode, data.RaceNumber,
                data.GradeCode, data.SurfaceCode, data.DistanceMeters, data.DirectionCode,
                "Collected by data collection agent (bulk)");
        }

        if (data.EntryCount is > 0 && _state.Status == RaceStatus.Draft)
            PublishCard(data.EntryCount.Value);

        foreach (var entry in data.Entries)
        {
            if (_state.Entries.Any(x => x.EntryId == entry.EntryId))
                continue;

            RegisterEntry(entry.EntryId, entry.HorseId, entry.HorseNumber,
                entry.JockeyId, entry.TrainerId, entry.GateNumber, entry.AssignedWeight,
                entry.SexCode, entry.Age, entry.DeclaredWeight, entry.DeclaredWeightDiff,
                entry.RunningStyleCode, entry.OwnerName);
        }

        if (!string.IsNullOrWhiteSpace(data.WinningHorseName)
            && _state.Status < RaceStatus.ResultDeclared)
            DeclareResult(data.WinningHorseName, data.DeclaredAt!.Value,
                stewardReportText: data.StewardReportText);

        foreach (var result in data.EntryResults)
        {
            if (_state.EntryResults.LastOrDefault(item => item.EntryId == result.EntryId) == result)
                continue;
            DeclareEntryResult(result.EntryId, result.FinishPosition, result.OfficialTime,
                result.MarginText, result.LastThreeFurlongTime, result.AbnormalResultCode,
                result.PrizeMoney, result.CornerPositions, result.AdditionalPrizeMoney,
                result.Popularity, result.OriginalFinishPosition, result.IsDeadHeat,
                result.Average1F,
                _state.Entries.Single(entry => entry.EntryId == result.EntryId).HorseId,
                _state.Entries.Single(entry => entry.EntryId == result.EntryId).JockeyId);
        }

        if (data.Weather is { } weather && _state.WeatherObservations.LastOrDefault() != weather)
            RecordWeatherObservation(weather.ObservationTime, weather.WeatherCode, weather.WeatherText,
                weather.TemperatureCelsius, weather.HumidityPercent,
                weather.WindDirectionCode, weather.WindSpeedMeterPerSecond);

        if (data.TrackCondition is { } track && _state.TrackConditionObservations.LastOrDefault() != track)
            RecordTrackConditionObservation(track.ObservationTime, track.TurfConditionCode,
                track.DirtConditionCode, track.GoingDescriptionText);

        if (data.Payouts is { } payout && _state.Status < RaceStatus.PayoutDeclared)
            DeclarePayoutResult(payout.DeclaredAt, payout.WinPayouts, payout.PlacePayouts,
                payout.QuinellaPayouts, payout.ExactaPayouts, payout.TrifectaPayouts);
    }

    private void ValidateBulkRaceResult(BulkRaceResultData data)
    {
        ArgumentNullException.ThrowIfNull(data.Entries);
        ArgumentNullException.ThrowIfNull(data.EntryResults);
        if (data.RaceDate == default) throw new ArgumentException("Race date is required.", nameof(data));
        if (string.IsNullOrWhiteSpace(data.RacecourseCode)) throw new ArgumentException("Racecourse code is required.", nameof(data));
        if (data.RaceNumber <= 0) throw new ArgumentException("Race number must be positive.", nameof(data));
        if (string.IsNullOrWhiteSpace(data.RaceName)) throw new ArgumentException("Race name is required.", nameof(data));
        if (data.EntryCount is <= 0) throw new ArgumentException("Entry count must be positive when specified.", nameof(data));
        if (!string.IsNullOrWhiteSpace(data.WinningHorseName) && data.DeclaredAt is null)
            throw new ArgumentException("Declared time is required with a winning horse.", nameof(data));

        if (data.Entries.Any(x => string.IsNullOrWhiteSpace(x.EntryId)
                                  || string.IsNullOrWhiteSpace(x.HorseId)
                                  || x.HorseNumber <= 0))
            throw new ArgumentException("Every entry requires an entry ID, horse ID, and positive horse number.", nameof(data));
        if (data.Entries.Select(x => x.EntryId).Distinct(StringComparer.Ordinal).Count() != data.Entries.Count)
            throw new ArgumentException("Entry IDs must be unique.", nameof(data));
        if (data.Entries.Select(x => x.HorseNumber).Distinct().Count() != data.Entries.Count)
            throw new ArgumentException("Horse numbers must be unique.", nameof(data));
        if (data.EntryResults.Any(x => string.IsNullOrWhiteSpace(x.EntryId)))
            throw new ArgumentException("Every entry result requires an entry ID.", nameof(data));
        if (data.EntryResults.Select(x => x.EntryId).Distinct(StringComparer.Ordinal).Count() != data.EntryResults.Count)
            throw new ArgumentException("Entry result IDs must be unique.", nameof(data));

        var knownEntryIds = _state.Entries.Select(x => x.EntryId)
            .Concat(data.Entries.Select(x => x.EntryId)).ToHashSet(StringComparer.Ordinal);
        if (data.EntryResults.Any(x => !knownEntryIds.Contains(x.EntryId)))
            throw new ArgumentException("Every entry result must reference a registered or incoming entry.", nameof(data));

        var effectiveStatus = _state.Status;
        if (effectiveStatus == RaceStatus.Draft && data.EntryCount is > 0)
            effectiveStatus = RaceStatus.CardPublished;
        if (!string.IsNullOrWhiteSpace(data.WinningHorseName) && effectiveStatus < RaceStatus.ResultDeclared)
            effectiveStatus = RaceStatus.ResultDeclared;
        if (data.Entries.Count > 0 && effectiveStatus == RaceStatus.Draft)
            throw new ArgumentException("Entries require a published race card.", nameof(data));
        if (data.EntryResults.Count > 0 && effectiveStatus < RaceStatus.ResultDeclared)
            throw new ArgumentException("Entry results require a declared race result.", nameof(data));
        if (data.Payouts is not null && effectiveStatus < RaceStatus.ResultDeclared)
            throw new ArgumentException("Payouts require a declared race result.", nameof(data));
    }
}
