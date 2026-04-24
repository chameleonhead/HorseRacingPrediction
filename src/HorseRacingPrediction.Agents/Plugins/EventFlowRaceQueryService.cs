using EventFlow.Queries;
using EventFlow.ReadStores.InMemory;
using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// EventFlow の <see cref="IQueryProcessor"/> を使って <see cref="IRaceQueryService"/> を実装するクラス。
/// </summary>
public sealed class EventFlowRaceQueryService : IRaceQueryService
{
    private readonly IQueryProcessor _queryProcessor;

    public EventFlowRaceQueryService(IQueryProcessor queryProcessor)
    {
        _queryProcessor = queryProcessor;
    }

    public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(
        string raceId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId);
        return _queryProcessor.ProcessAsync(query, cancellationToken)!;
    }

    public Task<HorseReadModel?> GetHorseAsync(
        string horseId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<HorseReadModel>(horseId);
        return _queryProcessor.ProcessAsync(query, cancellationToken)!;
    }

    public Task<JockeyReadModel?> GetJockeyAsync(
        string jockeyId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<JockeyReadModel>(jockeyId);
        return _queryProcessor.ProcessAsync(query, cancellationToken)!;
    }

    public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(
        string subjectType, string subjectId, CancellationToken cancellationToken = default)
    {
        var key = MemoBySubjectLocator.MakeKey(
            Enum.Parse<HorseRacingPrediction.Domain.Memos.MemoSubjectType>(subjectType, ignoreCase: true),
            subjectId);
        var query = new ReadModelByIdQuery<MemoBySubjectReadModel>(key);
        return _queryProcessor.ProcessAsync(query, cancellationToken)!;
    }

    public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(
        string horseId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<HorseRaceHistoryReadModel>(horseId);
        return _queryProcessor.ProcessAsync(query, cancellationToken)!;
    }

    public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(
        string jockeyId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<JockeyRaceHistoryReadModel>(jockeyId);
        return _queryProcessor.ProcessAsync(query, cancellationToken)!;
    }
}
