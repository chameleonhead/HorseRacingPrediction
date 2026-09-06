namespace HorseRacingPrediction.ApiClient;

/// <summary>
/// レース1件分の作成/更新・確定結果・全馬の着順・天候・馬場状態・払戻を
/// 1回のAPI呼び出しでまとめて登録するためのリクエストモデル。
/// <see cref="IDataCollectionWriteService.DeclareRaceResultBulkAsync"/> 参照。
/// </summary>
public sealed record RaceResultBulkRequest(
    string RaceDate,
    string RacecourseCode,
    int RaceNumber,
    string RaceName,
    int? EntryCount,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    string? WinningHorseName,
    DateTimeOffset? DeclaredAt,
    IReadOnlyList<RaceResultBulkEntry>? Entries,
    RaceResultBulkWeather? Weather,
    RaceResultBulkTrackCondition? TrackCondition,
    RaceResultBulkPayouts? Payouts);

public sealed record RaceResultBulkEntry(
    int HorseNumber,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney);

public sealed record RaceResultBulkWeather(
    DateTimeOffset ObservationTime,
    string? WeatherCode,
    string? WeatherText,
    decimal? TemperatureCelsius,
    decimal? HumidityPercent,
    string? WindDirectionCode,
    decimal? WindSpeedMeterPerSecond);

public sealed record RaceResultBulkTrackCondition(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);

public sealed record RaceResultBulkPayoutEntry(string Combination, decimal Amount);

public sealed record RaceResultBulkPayouts(
    DateTimeOffset DeclaredAt,
    IReadOnlyList<RaceResultBulkPayoutEntry>? WinPayouts,
    IReadOnlyList<RaceResultBulkPayoutEntry>? PlacePayouts,
    IReadOnlyList<RaceResultBulkPayoutEntry>? QuinellaPayouts,
    IReadOnlyList<RaceResultBulkPayoutEntry>? ExactaPayouts,
    IReadOnlyList<RaceResultBulkPayoutEntry>? TrifectaPayouts);

/// <summary>
/// 一括登録の結果。個々の項目（結果宣言・各馬の成績・天候・馬場状態・払戻）は
/// 1件失敗しても他の項目の登録は継続するため、失敗した項目は例外にせず
/// <see cref="Errors"/> に文言として集約して返す。
/// </summary>
public sealed record RaceResultBulkOutcome(string RaceId, IReadOnlyList<string> Errors);
