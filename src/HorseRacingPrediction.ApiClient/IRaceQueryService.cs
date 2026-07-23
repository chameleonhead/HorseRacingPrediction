using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.ApiClient;

/// <summary>
/// レース・馬・騎手に関する読み取りクエリを抽象化するサービスインターフェース。
/// <para>
/// 実行環境ごとに具体実装を差し替えることで、
/// エージェントコードを変更せずにデータソースを切り替えられる。
/// </para>
/// </summary>
public interface IRaceQueryService
{
    Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(
        DateOnly raceDate, CancellationToken cancellationToken = default);

    Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(
        string raceId, CancellationToken cancellationToken = default);

    Task<HorseReadModel?> GetHorseAsync(
        string horseId, CancellationToken cancellationToken = default);

    Task<JockeyReadModel?> GetJockeyAsync(
        string jockeyId, CancellationToken cancellationToken = default);

    Task<TrainerReadModel?> GetTrainerAsync(
        string trainerId, CancellationToken cancellationToken = default)
        => Task.FromResult<TrainerReadModel?>(null);

    Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(
        string subjectType, string subjectId, CancellationToken cancellationToken = default);

    Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(
        string horseId, CancellationToken cancellationToken = default);

    Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(
        string jockeyId, CancellationToken cancellationToken = default);

    Task<MlPredictionResponse?> GetMlPredictionAsync(
        string raceId, CancellationToken cancellationToken = default);

    /// <summary>指定した予測票 ID の確定済み予測票（印・スコア・コメント）を取得する。</summary>
    Task<PredictionTicketSummaryReadModel?> GetPredictionTicketAsync(
        string predictionTicketId, CancellationToken cancellationToken = default);
}
