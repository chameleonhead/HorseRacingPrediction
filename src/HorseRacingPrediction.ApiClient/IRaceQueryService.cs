
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.MachineLearning;
using HorseRacingPrediction.Contracts.Memos;
using HorseRacingPrediction.Contracts.Predictions;
using HorseRacingPrediction.Contracts.Races;
using HorseRacingPrediction.Contracts.Trainers;

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
    Task<IReadOnlyList<RaceSearchSummaryDto>> SearchRegisteredRacesAsync(
        DateOnly raceDate, CancellationToken cancellationToken = default);

    Task<RacePredictionContextDto?> GetRacePredictionContextAsync(
        string raceId, CancellationToken cancellationToken = default);

    Task<HorseDto?> GetHorseAsync(
        string horseId, CancellationToken cancellationToken = default);

    Task<JockeyDto?> GetJockeyAsync(
        string jockeyId, CancellationToken cancellationToken = default);

    Task<TrainerDto?> GetTrainerAsync(
        string trainerId, CancellationToken cancellationToken = default)
        => Task.FromResult<TrainerDto?>(null);

    Task<MemoBySubjectDto?> GetMemosBySubjectAsync(
        string subjectType, string subjectId, CancellationToken cancellationToken = default);

    Task<HorseRaceHistoryDto?> GetHorseRaceHistoryAsync(
        string horseId, CancellationToken cancellationToken = default);

    Task<JockeyRaceHistoryDto?> GetJockeyRaceHistoryAsync(
        string jockeyId, CancellationToken cancellationToken = default);

    Task<MlPredictionDto?> GetMlPredictionAsync(
        string raceId, CancellationToken cancellationToken = default);

    /// <summary>指定した予測票 ID の確定済み予測票（印・スコア・コメント）を取得する。</summary>
    Task<PredictionTicketWithMarksDto?> GetPredictionTicketAsync(
        string predictionTicketId, CancellationToken cancellationToken = default);
}
