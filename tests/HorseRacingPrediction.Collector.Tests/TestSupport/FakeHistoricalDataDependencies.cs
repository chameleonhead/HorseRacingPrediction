using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Collector.Tests.TestSupport;

/// <summary>
/// <see cref="HistoricalDataRequestPlanner"/> をテストで組み立てるための最小限のダミー。
/// <see cref="GetRacePredictionContextAsync"/> が常に null を返すため、Plannerは追加要求を
/// 一切スケジュールせずに no-op で完了する。
/// </summary>
internal sealed class NullRaceQueryService : IRaceQueryService
{
    public Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RaceSearchSummary>>(Array.Empty<RaceSearchSummary>());

    public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
        => Task.FromResult<RacePredictionContextReadModel?>(null);

    public Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
        => Task.FromResult<HorseReadModel?>(null);

    public Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
        => Task.FromResult<JockeyReadModel?>(null);

    public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
        => Task.FromResult<MemoBySubjectReadModel?>(null);

    public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
        => Task.FromResult<HorseRaceHistoryReadModel?>(null);

    public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
        => Task.FromResult<JockeyRaceHistoryReadModel?>(null);

    public Task<MlPredictionResponse?> GetMlPredictionAsync(string raceId, CancellationToken cancellationToken = default)
        => Task.FromResult<MlPredictionResponse?>(null);

    public Task<PredictionTicketSummaryReadModel?> GetPredictionTicketAsync(string predictionTicketId, CancellationToken cancellationToken = default)
        => Task.FromResult<PredictionTicketSummaryReadModel?>(null);
}

internal sealed class NullHistoricalRaceReferenceCollector : IHistoricalRaceReferenceCollector
{
    public Task<IReadOnlyList<HistoricalRaceReference>> CollectAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<HistoricalRaceReference>>(Array.Empty<HistoricalRaceReference>());
}
