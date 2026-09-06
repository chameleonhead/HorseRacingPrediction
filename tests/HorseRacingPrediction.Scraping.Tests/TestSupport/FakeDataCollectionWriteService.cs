using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

/// <summary>
/// Workflow層のテスト用フェイク。呼び出された内容を記録し、決定論的な ID を返す。
/// </summary>
internal sealed class FakeDataCollectionWriteService : IDataCollectionWriteService
{
    public sealed record UpsertRaceCall(
        string RaceDate,
        string RacecourseCode,
        int RaceNumber,
        string RaceName,
        int? EntryCount);

    public sealed record UpsertRaceEntryCall(
        string RaceId,
        int HorseNumber,
        string HorseName,
        string? JockeyName,
        string? TrainerName,
        int? GateNumber,
        decimal? AssignedWeight);

    public List<UpsertRaceCall> UpsertRaceCalls { get; } = [];

    public sealed record DeclareRaceEntryResultCall(
        string RaceId,
        int HorseNumber,
        int? FinishPosition,
        string? OfficialTime);

    public sealed record DeclareRaceResultCall(
        string RaceId,
        string WinningHorseName);

    public List<UpsertRaceEntryCall> UpsertRaceEntryCalls { get; } = [];

    public List<DeclareRaceEntryResultCall> DeclareRaceEntryResultCalls { get; } = [];

    public List<DeclareRaceResultCall> DeclareRaceResultCalls { get; } = [];

    /// <summary>設定した馬番で <see cref="DeclareRaceEntryResultAsync"/> が呼ばれた場合に例外を投げる（部分失敗のテスト用）。</summary>
    public int? FailForHorseNumber { get; set; }

    /// <summary><see cref="DeclareRaceResultAsync"/> が呼ばれた場合に例外を投げる（失敗系テスト用）。</summary>
    public bool FailDeclareRaceResult { get; set; }

    public Task<string> UpsertRaceAsync(
        string raceDate,
        string racecourseCode,
        int raceNumber,
        string raceName,
        int? entryCount,
        string? gradeCode,
        string? surfaceCode,
        int? distanceMeters,
        string? directionCode,
        CancellationToken cancellationToken = default)
    {
        UpsertRaceCalls.Add(new UpsertRaceCall(raceDate, racecourseCode, raceNumber, raceName, entryCount));
        return Task.FromResult($"race-{raceDate}-{racecourseCode}-{raceNumber}");
    }

    public Task<string> UpsertHorseAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"horse-{registeredName}");

    public sealed record UpsertHorseWithOwnerCall(string RegisteredName, string? OwnerName);

    public List<UpsertHorseWithOwnerCall> UpsertHorseWithOwnerCalls { get; } = [];

    public Task<string> UpsertHorseWithOwnerAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        string? ownerName,
        CancellationToken cancellationToken = default)
    {
        UpsertHorseWithOwnerCalls.Add(new UpsertHorseWithOwnerCall(registeredName, ownerName));
        return Task.FromResult($"horse-{registeredName}");
    }

    public Task<string> UpsertJockeyAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"jockey-{displayName}");

    public Task<string> UpsertTrainerAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"trainer-{displayName}");

    public Task<string> UpsertRaceEntryAsync(
        string raceId,
        int horseNumber,
        string horseName,
        string? jockeyName,
        string? trainerName,
        int? gateNumber,
        decimal? assignedWeight,
        string? sexCode,
        int? age,
        decimal? declaredWeight,
        decimal? declaredWeightDiff,
        CancellationToken cancellationToken = default)
    {
        UpsertRaceEntryCalls.Add(new UpsertRaceEntryCall(
            raceId, horseNumber, horseName, jockeyName, trainerName, gateNumber, assignedWeight));
        return Task.FromResult($"{raceId}-entry-{horseNumber}");
    }

    public Task<string> DeclareRaceResultAsync(
        string raceId,
        string winningHorseName,
        string? declaredAt,
        string? winningHorseId,
        CancellationToken cancellationToken = default)
    {
        if (FailDeclareRaceResult)
        {
            throw new InvalidOperationException("テスト用の失敗: DeclareRaceResultAsync");
        }

        DeclareRaceResultCalls.Add(new DeclareRaceResultCall(raceId, winningHorseName));
        return Task.FromResult("declared");
    }

    public Task<string> DeclareRaceEntryResultAsync(
        string raceId,
        int horseNumber,
        int? finishPosition,
        string? officialTime,
        string? marginText,
        string? lastThreeFurlongTime,
        string? abnormalResultCode,
        decimal? prizeMoney,
        CancellationToken cancellationToken = default)
    {
        if (FailForHorseNumber == horseNumber)
        {
            throw new InvalidOperationException($"テスト用の失敗: HorseNumber={horseNumber}");
        }

        DeclareRaceEntryResultCalls.Add(new DeclareRaceEntryResultCall(
            raceId, horseNumber, finishPosition, officialTime));
        return Task.FromResult("declared");
    }

    public sealed record DeclareRacePayoutsCall(
        string RaceId,
        string? WinPayoutsJson,
        string? PlacePayoutsJson,
        string? QuinellaPayoutsJson,
        string? ExactaPayoutsJson,
        string? TrifectaPayoutsJson);

    public sealed record RecordWeatherObservationCall(string RaceId, string? WeatherText);

    public sealed record RecordTrackConditionObservationCall(string RaceId, string? GoingDescriptionText);

    public List<DeclareRacePayoutsCall> DeclareRacePayoutsCalls { get; } = [];

    public List<RecordWeatherObservationCall> RecordWeatherObservationCalls { get; } = [];

    public List<RecordTrackConditionObservationCall> RecordTrackConditionObservationCalls { get; } = [];

    public Task<string> DeclareRacePayoutsAsync(
        string raceId,
        string? winPayoutsJson,
        string? placePayoutsJson,
        string? quinellaPayoutsJson,
        string? exactaPayoutsJson,
        string? trifectaPayoutsJson,
        CancellationToken cancellationToken = default)
    {
        DeclareRacePayoutsCalls.Add(new DeclareRacePayoutsCall(
            raceId, winPayoutsJson, placePayoutsJson, quinellaPayoutsJson, exactaPayoutsJson, trifectaPayoutsJson));
        return Task.FromResult("declared");
    }

    public Task<string> RecordWeatherObservationAsync(
        string raceId,
        DateTimeOffset observationTime,
        string? weatherCode,
        string? weatherText,
        decimal? temperatureCelsius,
        decimal? humidityPercent,
        string? windDirectionCode,
        decimal? windSpeedMeterPerSecond,
        CancellationToken cancellationToken = default)
    {
        RecordWeatherObservationCalls.Add(new RecordWeatherObservationCall(raceId, weatherText));
        return Task.FromResult("recorded");
    }

    public Task<string> RecordTrackConditionObservationAsync(
        string raceId,
        DateTimeOffset observationTime,
        string? turfConditionCode,
        string? dirtConditionCode,
        string? goingDescriptionText,
        CancellationToken cancellationToken = default)
    {
        RecordTrackConditionObservationCalls.Add(new RecordTrackConditionObservationCall(raceId, goingDescriptionText));
        return Task.FromResult("recorded");
    }

    public List<RaceResultBulkRequest> DeclareRaceResultBulkCalls { get; } = [];

    /// <summary>
    /// 実際の /api/races/result-bulk エンドポイントと同様、レース確定宣言・各馬の成績・
    /// 天候・馬場状態・払戻は1件失敗しても他の項目の登録を継続し、失敗内容は
    /// <see cref="RaceResultBulkOutcome.Errors"/> に集約する挙動をインメモリで再現する。
    /// <see cref="FailForHorseNumber"/>・<see cref="FailDeclareRaceResult"/> による
    /// 部分失敗テストは、既存の個別Call記録（<see cref="DeclareRaceEntryResultCalls"/>等）
    /// を引き続き利用できるよう、この一括呼び出しの中でも同じリストに記録する。
    /// </summary>
    public Task<RaceResultBulkOutcome> DeclareRaceResultBulkAsync(
        RaceResultBulkRequest request,
        CancellationToken cancellationToken = default)
    {
        DeclareRaceResultBulkCalls.Add(request);

        var raceId = DeterministicIdGenerator.BuildRaceId(
            DateOnly.Parse(request.RaceDate), request.RacecourseCode, request.RaceNumber);
        UpsertRaceCalls.Add(new UpsertRaceCall(request.RaceDate, request.RacecourseCode, request.RaceNumber, request.RaceName, request.EntryCount));

        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.WinningHorseName))
        {
            if (FailDeclareRaceResult)
            {
                errors.Add("レース確定宣言エラー: テスト用の失敗: DeclareRaceResultAsync");
            }
            else
            {
                DeclareRaceResultCalls.Add(new DeclareRaceResultCall(raceId, request.WinningHorseName));
            }
        }

        if (request.Entries is not null)
        {
            foreach (var entry in request.Entries)
            {
                if (FailForHorseNumber == entry.HorseNumber)
                {
                    errors.Add($"着順記録エラー: HorseNumber={entry.HorseNumber} — テスト用の失敗: HorseNumber={entry.HorseNumber}");
                    continue;
                }

                DeclareRaceEntryResultCalls.Add(new DeclareRaceEntryResultCall(
                    raceId, entry.HorseNumber, entry.FinishPosition, entry.OfficialTime));
            }
        }

        if (request.Weather is not null)
        {
            RecordWeatherObservationCalls.Add(new RecordWeatherObservationCall(raceId, request.Weather.WeatherText));
        }

        if (request.TrackCondition is not null)
        {
            RecordTrackConditionObservationCalls.Add(new RecordTrackConditionObservationCall(raceId, request.TrackCondition.GoingDescriptionText));
        }

        if (request.Payouts is not null)
        {
            DeclareRacePayoutsCalls.Add(new DeclareRacePayoutsCall(
                raceId,
                SerializePayouts(request.Payouts.WinPayouts),
                SerializePayouts(request.Payouts.PlacePayouts),
                SerializePayouts(request.Payouts.QuinellaPayouts),
                SerializePayouts(request.Payouts.ExactaPayouts),
                SerializePayouts(request.Payouts.TrifectaPayouts)));
        }

        return Task.FromResult(new RaceResultBulkOutcome(raceId, errors));
    }

    private static string? SerializePayouts(IReadOnlyList<RaceResultBulkPayoutEntry>? payouts) =>
        payouts is null || payouts.Count == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(payouts.Select(p => new { combination = p.Combination, amount = p.Amount }));
}
