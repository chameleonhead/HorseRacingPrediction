namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// 予測票の作成・更新操作を抽象化するサービスインターフェース。
/// <para>
/// ローカルテスト時は <see cref="EventFlowPredictionWriteService"/> を使用する。
/// クラウド接続時は HTTP クライアント実装に差し替えることで、
/// エージェントコードを変更せずにバックエンドを切り替えられる。
/// </para>
/// </summary>
public interface IPredictionWriteService
{
    /// <summary>新しい予測票を作成し、発行された予測票 ID を返す。</summary>
    Task<string> CreatePredictionTicketAsync(
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment,
        CancellationToken cancellationToken = default);

    /// <summary>予測票に出走馬の予測印を追加する。</summary>
    Task AddPredictionMarkAsync(
        string predictionTicketId,
        string entryId,
        string markCode,
        int predictedRank,
        decimal score,
        string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>予測票に根拠・シグナルを追加する。</summary>
    Task AddPredictionRationaleAsync(
        string predictionTicketId,
        string subjectType,
        string subjectId,
        string signalType,
        string? signalValue,
        string? explanationText,
        CancellationToken cancellationToken = default);

    /// <summary>予測票を確定する。確定後は変更不可。</summary>
    Task FinalizePredictionTicketAsync(
        string predictionTicketId,
        CancellationToken cancellationToken = default);
}
