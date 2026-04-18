using System.Collections.Concurrent;
using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class PredictionComparisonViewLocator : IReadModelLocator
{
    private readonly ConcurrentDictionary<string, string> _predictionToRaceMap = new();

    public IEnumerable<string> GetReadModelIds(IDomainEvent domainEvent)
    {
        if (domainEvent.GetIdentity() is RaceId raceId)
        {
            yield return raceId.Value;
            yield break;
        }

        if (domainEvent is IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketCreated> created)
        {
            var raceId2 = created.AggregateEvent.RaceId;
            _predictionToRaceMap[created.AggregateIdentity.Value] = raceId2;
            yield return raceId2;
            yield break;
        }

        if (domainEvent.GetIdentity() is PredictionTicketId predictionId &&
            _predictionToRaceMap.TryGetValue(predictionId.Value, out var mappedRaceId))
        {
            yield return mappedRaceId;
        }
    }
}
