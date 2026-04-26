using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// レース・馬・騎手に関する読み取りクエリを抽象化するサービスインターフェース。
/// <para>
/// ローカルテスト時は <see cref="EventFlowRaceQueryService"/> を使用する。
/// クラウド接続時は HTTP クライアント実装に差し替えることで、
/// エージェントコードを変更せずにデータソースを切り替えられる。
/// </para>
/// </summary>
public interface IRaceQueryService
{
    Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(
        string raceId, CancellationToken cancellationToken = default);

    Task<HorseReadModel?> GetHorseAsync(
        string horseId, CancellationToken cancellationToken = default);

    Task<JockeyReadModel?> GetJockeyAsync(
        string jockeyId, CancellationToken cancellationToken = default);

    Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(
        string subjectType, string subjectId, CancellationToken cancellationToken = default);

    Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(
        string horseId, CancellationToken cancellationToken = default);

    Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(
        string jockeyId, CancellationToken cancellationToken = default);
}
