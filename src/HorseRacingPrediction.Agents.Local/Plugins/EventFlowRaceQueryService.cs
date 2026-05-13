using EventFlow.EntityFramework;
using EventFlow.Queries;
using HorseRacingPrediction.Agents.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using MemoSubjectType = HorseRacingPrediction.Domain.Memos.MemoSubjectType;

namespace HorseRacingPrediction.Agents.Plugins;

public sealed class EventFlowRaceQueryService : IRaceQueryService
{
    private readonly IQueryProcessor _queryProcessor;
    private readonly IDbContextProvider<EventStoreDbContext>? _dbContextProvider;

    public EventFlowRaceQueryService(IQueryProcessor queryProcessor)
    {
        _queryProcessor = queryProcessor;
    }

    public EventFlowRaceQueryService(IQueryProcessor queryProcessor, IDbContextProvider<EventStoreDbContext> dbContextProvider)
    {
        _queryProcessor = queryProcessor;
        _dbContextProvider = dbContextProvider;
    }

    public async Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
    {
        if (_dbContextProvider is null)
            return [];

        using var dbContext = _dbContextProvider.CreateContext();
        var races = await dbContext.Set<AppReadModels.RaceSummaryReadModel>()
            .AsNoTracking()
            .Where(x => x.RaceDate.HasValue && x.RaceDate.Value == raceDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return races.Select(x => new RaceSearchSummary(x.RaceId, x.RaceDate, x.RacecourseCode, x.RaceNumber)).ToList();
    }

    public async Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
    {
        var model = await _queryProcessor.ProcessAsync(new ReadModelByIdQuery<AppReadModels.RacePredictionContextReadModel>(raceId), cancellationToken);
        return model is null ? null : new RacePredictionContextReadModel
        {
            RaceId = model.RaceId,
            RaceDate = model.RaceDate,
            RacecourseCode = model.RacecourseCode,
            RaceNumber = model.RaceNumber,
            RaceName = model.RaceName,
            Status = (RaceStatus)(int)model.Status,
            GradeCode = model.GradeCode,
            SurfaceCode = model.SurfaceCode,
            DistanceMeters = model.DistanceMeters,
            DirectionCode = model.DirectionCode,
            Entries = model.Entries.Select(x => new RacePredictionContextEntry(x.EntryId, x.HorseId, x.HorseNumber, x.JockeyId, x.TrainerId, x.GateNumber, x.AssignedWeight, x.SexCode, x.Age, x.DeclaredWeight, x.DeclaredWeightDiff, x.RunningStyleCode)).ToList(),
            WeatherObservations = model.WeatherObservations.Select(x => new WeatherObservationSnapshot(x.ObservationTime, x.WeatherCode, x.WeatherText, x.TemperatureCelsius, x.HumidityPercent, x.WindDirectionCode, x.WindSpeedMeterPerSecond)).ToList(),
            TrackConditionObservations = model.TrackConditionObservations.Select(x => new TrackConditionSnapshot(x.ObservationTime, x.TurfConditionCode, x.DirtConditionCode, x.GoingDescriptionText)).ToList()
        };
    }

    public async Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
    {
        var model = await _queryProcessor.ProcessAsync(new ReadModelByIdQuery<AppReadModels.HorseReadModel>(horseId), cancellationToken);
        return model is null ? null : new HorseReadModel
        {
            HorseId = model.HorseId,
            RegisteredName = model.RegisteredName,
            NormalizedName = model.NormalizedName,
            SexCode = model.SexCode,
            BirthDate = model.BirthDate,
            Aliases = model.Aliases.Select(x => new HorseAliasEntry(x.AliasType, x.AliasValue, x.SourceName, x.IsPrimary)).ToList()
        };
    }

    public async Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
    {
        var model = await _queryProcessor.ProcessAsync(new ReadModelByIdQuery<AppReadModels.JockeyReadModel>(jockeyId), cancellationToken);
        return model is null ? null : new JockeyReadModel
        {
            JockeyId = model.JockeyId,
            DisplayName = model.DisplayName,
            NormalizedName = model.NormalizedName,
            AffiliationCode = model.AffiliationCode,
            Aliases = model.Aliases.Select(x => new JockeyAliasEntry(x.AliasType, x.AliasValue, x.SourceName, x.IsPrimary)).ToList()
        };
    }

    public async Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
    {
        var key = AppReadModels.MemoBySubjectLocator.MakeKey(Enum.Parse<MemoSubjectType>(subjectType, ignoreCase: true), subjectId);
        var model = await _queryProcessor.ProcessAsync(new ReadModelByIdQuery<AppReadModels.MemoBySubjectReadModel>(key), cancellationToken);
        return model is null ? null : new MemoBySubjectReadModel
        {
            SubjectKey = model.SubjectKey,
            Memos = model.Memos.Select(m => new MemoSnapshot(m.MemoId, m.AuthorId, m.MemoType, m.Content, m.CreatedAt, m.Subjects.Select(s => new MemoSubjectSnapshot(s.SubjectType, s.SubjectId)).ToList(), m.Links.Select(l => new MemoLinkSnapshot(l.LinkId, l.LinkType, l.Title, l.Url, l.StorageKey)).ToList())).ToList()
        };
    }

    public async Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
    {
        var model = await _queryProcessor.ProcessAsync(new ReadModelByIdQuery<AppReadModels.HorseRaceHistoryReadModel>(horseId), cancellationToken);
        return model is null ? null : new HorseRaceHistoryReadModel
        {
            HorseId = model.HorseId,
            Entries = model.Entries.Select(x => new HorseRaceHistoryEntry(x.RaceId, x.EntryId, x.RaceDate, x.RacecourseCode, x.SurfaceCode, x.DistanceMeters, x.DirectionCode, x.GradeCode, x.GateNumber, x.AssignedWeight, x.DeclaredWeight, x.DeclaredWeightDiff, x.RunningStyleCode, x.JockeyId, x.TrainerId, x.FinishPosition, x.LastThreeFurlongTime, x.CornerPositions, x.PrizeMoney)).ToList()
        };
    }

    public async Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
    {
        var model = await _queryProcessor.ProcessAsync(new ReadModelByIdQuery<AppReadModels.JockeyRaceHistoryReadModel>(jockeyId), cancellationToken);
        return model is null ? null : new JockeyRaceHistoryReadModel
        {
            JockeyId = model.JockeyId,
            Entries = model.Entries.Select(x => new JockeyRaceHistoryEntry(x.RaceId, x.EntryId, x.HorseId, x.RaceDate, x.RacecourseCode, x.SurfaceCode, x.DistanceMeters, x.DirectionCode, x.GradeCode, x.FinishPosition, x.PrizeMoney)).ToList()
        };
    }
}