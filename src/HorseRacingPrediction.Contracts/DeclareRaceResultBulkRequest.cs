using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts;

/// <summary>
/// レース1件分の作成/更新・確定結果・全馬の着順・天候・馬場状態・払戻を
/// 1回のAPI呼び出しでまとめて登録するための一括登録リクエスト。
/// <para>
/// Api（サーバー側エンドポイント）とApiClient（Collectorが使うHTTPクライアント実装）の
/// 双方から参照される共有DTOであるため、両者でDTOの形が乖離しないよう
/// このプロジェクト（HorseRacingPrediction.Contracts）で一元管理する。
/// </para>
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
    DeclarePayoutResultRequest? Payouts = null,
    string? TargetRaceId = null, bool RefreshExistingData = false, bool IsRaceCard = false,
    TimeOnly? StartTime = null, string? OverallPaceText = null, string? CornerPassagesText = null, string? CourseLayout = null);

/// <summary>
/// 出走馬1頭分の成績情報。
/// <paramref name="HorseName"/> 以下の出走馬属性（依頼書4節・14節）は、
/// RaceCard（出馬表）を経由せずレース結果のみから収集した過去レースでも
/// RaceEntry相当の情報（馬名・性齢・斤量・騎手・調教師等）を復元できるように、
/// 出馬表登録と同じ命名・null許容パターンで追加した項目。
/// RaceCardが別途取得済みの場合はそちらの情報を優先してよく、本項目は
/// 「結果ページからのみ取得できた場合のフォールバック」として送信する。
/// </summary>
public sealed record RaceResultEntryBulkDto(
    int HorseNumber,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney,
    string? HorseName = null,
    string? JockeyName = null,
    string? TrainerName = null,
    int? GateNumber = null,
    decimal? AssignedWeight = null,
    string? SexCode = null,
    int? Age = null,
    int? Popularity = null,
    int? BodyWeight = null,
    int? BodyWeightChange = null,
    int? OriginalFinishPosition = null,
    bool IsDeadHeat = false, string? OwnerName = null, string? CornerPositions = null, decimal? Average1F = null);
