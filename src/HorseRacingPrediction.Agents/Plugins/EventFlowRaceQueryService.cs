using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EventFlow.EntityFramework;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// EventFlow の <see cref="IQueryProcessor"/> を使って <see cref="IRaceQueryService"/> を実装するクラス。
/// </summary>
public sealed class EventFlowRaceQueryService : IRaceQueryService
{
    private readonly IQueryProcessor _queryProcessor;
    private readonly IDbContextProvider<EventStoreDbContext>? _dbContextProvider;

    public EventFlowRaceQueryService(IQueryProcessor queryProcessor)
    {
        _queryProcessor = queryProcessor;
    }

    public EventFlowRaceQueryService(
        IQueryProcessor queryProcessor,
        IDbContextProvider<EventStoreDbContext> dbContextProvider)
    {
        _queryProcessor = queryProcessor;
        _dbContextProvider = dbContextProvider;
    }

    public async Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
    {
        if (_dbContextProvider is null)
            return [];

        using var dbContext = _dbContextProvider.CreateContext();
        var races = await dbContext.Set<RaceSummaryReadModel>()
            .AsNoTracking()
            .Where(x => x.RaceDate.HasValue && x.RaceDate.Value == raceDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return races
            .Select(x => new RaceSearchSummary(x.RaceId, x.RaceDate, x.RacecourseCode, x.RaceNumber))
            .ToList();
    }

    public async Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(
        string raceId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId);
        return await _queryProcessor.ProcessAsync(query, cancellationToken);
    }

    public async Task<HorseReadModel?> GetHorseAsync(
        string horseId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<HorseReadModel>(horseId);
        return await _queryProcessor.ProcessAsync(query, cancellationToken);
    }

    public async Task<JockeyReadModel?> GetJockeyAsync(
        string jockeyId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<JockeyReadModel>(jockeyId);
        return await _queryProcessor.ProcessAsync(query, cancellationToken);
    }

    public async Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(
        string subjectType, string subjectId, CancellationToken cancellationToken = default)
    {
        var key = MemoBySubjectLocator.MakeKey(
            Enum.Parse<HorseRacingPrediction.Domain.Memos.MemoSubjectType>(subjectType, ignoreCase: true),
            subjectId);
        var query = new ReadModelByIdQuery<MemoBySubjectReadModel>(key);
        return await _queryProcessor.ProcessAsync(query, cancellationToken);
    }

    public async Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(
        string horseId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<HorseRaceHistoryReadModel>(horseId);
        return await _queryProcessor.ProcessAsync(query, cancellationToken);
    }

    public async Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(
        string jockeyId, CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<JockeyRaceHistoryReadModel>(jockeyId);
        return await _queryProcessor.ProcessAsync(query, cancellationToken);
    }
}
