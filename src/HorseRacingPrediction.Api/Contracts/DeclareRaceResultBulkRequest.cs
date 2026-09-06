using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

/// <summary>
/// レース1件分の作成/更新・確定結果・全馬の着順・天候・馬場状態・払戻を
/// 1回のAPI呼び出しでまとめて登録するための一括登録リクエスト。
/// </summary>
public sealed record DeclareRaceResultBulkRequest(
    [property: Required] DateOnly RaceDate,
    [property: Required, StringLength(32, MinimumLength = 2)] string RacecourseCode,
    [property: Range(1, 20)] int RaceNumber,
    [property: Required, StringLength(128, MinimumLength = 1)] string RaceName,
    int? EntryCount = null,
    string? GradeCode = null,
    string? SurfaceCode = null,
    int? DistanceMeters = null,
    string? DirectionCode = null,
    string? WinningHorseName = null,
    DateTimeOffset? DeclaredAt = null,
    IReadOnlyList<RaceResultEntryBulkDto>? Entries = null,
    RecordWeatherObservationRequest? Weather = null,
    RecordTrackConditionRequest? TrackCondition = null,
    DeclarePayoutResultRequest? Payouts = null);

public sealed record RaceResultEntryBulkDto(
    [property: Range(1, 28)] int HorseNumber,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney);
