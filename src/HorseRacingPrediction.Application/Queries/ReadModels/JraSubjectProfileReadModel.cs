using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed class JraSubjectProfileReadModel : IReadModel,
    IAmReadModelFor<HorseAggregate, HorseId, HorseJraProfileCollected>,
    IAmReadModelFor<TrainerAggregate, TrainerId, TrainerJraProfileCollected>
{
    public string SubjectId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string SourceIdentity { get; private set; } = string.Empty;
    public string SourceUrl { get; private set; } = string.Empty;
    public DateTimeOffset AcquiredAt { get; private set; }
    public Dictionary<string, string> Fields { get; private set; } = [];
    private void Apply(string id, CollectedSubjectProfile profile)
    {
        SubjectId = id; Name = profile.Name; SourceIdentity = profile.SourceIdentity;
        SourceUrl = profile.SourceUrl; AcquiredAt = profile.AcquiredAt;
        foreach (var item in profile.Fields.Where(x => !string.IsNullOrWhiteSpace(x.Value))) Fields[item.Key] = item.Value;
    }
    public Task ApplyAsync(IReadModelContext context, IDomainEvent<HorseAggregate, HorseId, HorseJraProfileCollected> e, CancellationToken token)
    { Apply(e.AggregateIdentity.Value, e.AggregateEvent.Profile); return Task.CompletedTask; }
    public Task ApplyAsync(IReadModelContext context, IDomainEvent<TrainerAggregate, TrainerId, TrainerJraProfileCollected> e, CancellationToken token)
    { Apply(e.AggregateIdentity.Value, e.AggregateEvent.Profile); return Task.CompletedTask; }
}
