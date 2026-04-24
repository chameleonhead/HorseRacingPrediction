namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// データ収集エージェントが行うドメインモデル更新操作を抽象化するサービスインターフェース。
/// <para>
/// ローカルテスト時は <see cref="EventFlowDataCollectionWriteService"/> を使用する。
/// クラウド接続時は HTTP クライアント実装に差し替えることで、
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
}
