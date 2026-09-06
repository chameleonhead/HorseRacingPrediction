namespace HorseRacingPrediction.ApiClient;

/// <summary>
/// データ収集エージェントが行うドメインモデル更新操作を抽象化するサービスインターフェース。
/// <para>
/// 実行環境ごとに具体実装を差し替えることで、
/// エージェントコードを変更せずにバックエンドを切り替えられる。
/// </para>
/// </summary>
public interface IDataCollectionWriteService
{
    /// <summary>レース情報を作成または更新し、レース ID を返す。</summary>
    Task<string> UpsertRaceAsync(
        string raceDate,
        string racecourseCode,
        int raceNumber,
        string raceName,
        int? entryCount,
        string? gradeCode,
        string? surfaceCode,
        int? distanceMeters,
        string? directionCode,
        CancellationToken cancellationToken = default);

    /// <summary>競走馬を作成または更新し、馬 ID を返す。</summary>
    Task<string> UpsertHorseAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        CancellationToken cancellationToken = default);

    Task<string> UpsertHorseWithOwnerAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        string? ownerName,
        CancellationToken cancellationToken = default)
        => UpsertHorseAsync(registeredName, normalizedName, sexCode, birthDate, cancellationToken);

    /// <summary>騎手を作成または更新し、騎手 ID を返す。</summary>
    Task<string> UpsertJockeyAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default);

    /// <summary>調教師を作成または更新し、調教師 ID を返す。</summary>
    Task<string> UpsertTrainerAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default);

    /// <summary>レースの出走エントリーを作成し、確認メッセージを返す。</summary>
    Task<string> UpsertRaceEntryAsync(
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
        CancellationToken cancellationToken = default);

    Task<string> UpsertRaceEntryAsync(
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
        string? ownerName,
        CancellationToken cancellationToken = default)
        => UpsertRaceEntryAsync(
            raceId, horseNumber, horseName, jockeyName, trainerName,
            gateNumber, assignedWeight, sexCode, age, declaredWeight,
            declaredWeightDiff, cancellationToken);

    /// <summary>レース全体の確定結果（勝ち馬）を宣言し、確認メッセージを返す。</summary>
    Task<string> DeclareRaceResultAsync(
        string raceId,
        string winningHorseName,
        string? declaredAt,
        string? winningHorseId,
        CancellationToken cancellationToken = default);

    /// <summary>出走馬 1 頭分の着順・タイムなどの成績を記録し、確認メッセージを返す。</summary>
    Task<string> DeclareRaceEntryResultAsync(
        string raceId,
        int horseNumber,
        int? finishPosition,
        string? officialTime,
        string? marginText,
        string? lastThreeFurlongTime,
        string? abnormalResultCode,
        decimal? prizeMoney,
        CancellationToken cancellationToken = default);

    /// <summary>払い戻しデータを記録し、確認メッセージを返す。</summary>
    Task<string> DeclareRacePayoutsAsync(
        string raceId,
        string? winPayoutsJson,
        string? placePayoutsJson,
        string? quinellaPayoutsJson,
        string? exactaPayoutsJson,
        string? trifectaPayoutsJson,
        CancellationToken cancellationToken = default);

    /// <summary>天候観測を記録し、確認メッセージを返す。</summary>
    Task<string> RecordWeatherObservationAsync(
        string raceId,
        DateTimeOffset observationTime,
        string? weatherCode,
        string? weatherText,
        decimal? temperatureCelsius,
        decimal? humidityPercent,
        string? windDirectionCode,
        decimal? windSpeedMeterPerSecond,
        CancellationToken cancellationToken = default);

    /// <summary>馬場状態観測を記録し、確認メッセージを返す。</summary>
    Task<string> RecordTrackConditionObservationAsync(
        string raceId,
        DateTimeOffset observationTime,
        string? turfConditionCode,
        string? dirtConditionCode,
        string? goingDescriptionText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// レース1件分の作成/更新・確定結果・全馬の着順・天候・馬場状態・払戻を
    /// 1回のAPI呼び出しでまとめて登録する。
    /// <para>
    /// <see cref="UpsertRaceAsync"/>・<see cref="DeclareRaceResultAsync"/>・
    /// <see cref="DeclareRaceEntryResultAsync"/>（馬の数だけ）・
    /// <see cref="RecordWeatherObservationAsync"/>・<see cref="RecordTrackConditionObservationAsync"/>・
    /// <see cref="DeclareRacePayoutsAsync"/>を個別に呼び出すと、レース1件あたり
    /// 10〜20回超のHTTPラウンドトリップが発生していたため、これらをまとめる。
    /// 個々の項目が失敗しても他の項目の登録は継続し、失敗内容は戻り値の
    /// <see cref="RaceResultBulkOutcome.Errors"/> に集約される。
    /// </para>
    /// </summary>
    Task<RaceResultBulkOutcome> DeclareRaceResultBulkAsync(
        RaceResultBulkRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// データの取得元となったJRAページのURLを、引用元（メモ機能のURLリンク）として
    /// 1件以上の対象（レース・馬・騎手・調教師）に紐付けて記録する。
    /// 同一レースについて複数回取得した場合、複数件を蓄積して残せる（上書きしない）。
    /// 記録自体の失敗は本体の収集処理を失敗させない（実装側で握りつぶす）。
    /// </summary>
    Task RecordSourceCitationAsync(
        IReadOnlyList<CitationSubject> subjects,
        string sourceUrl,
        string? title = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
