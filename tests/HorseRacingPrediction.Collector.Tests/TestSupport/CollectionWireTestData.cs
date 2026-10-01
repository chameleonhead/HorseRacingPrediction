using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Collector.Tests.TestSupport;

internal static class CollectionWireTestData
{
    internal static AcquireCollectionTaskResponse AcquiredTask(LeasedCollectionTask task)
        => new(new CollectionTaskAcquireResultDto(CollectionTaskAcquireStatus.Acquired,
            new LeasedCollectionTaskDto(task.TaskId, task.RequestId,
                new CollectionResourceKeyDto(task.Resource.Type, task.Resource.Provider, task.Resource.Id),
                new CollectionDefinitionIdDto(task.Definition.Value), task.RequestedRevision, task.Reason,
                task.Lane, task.Priority, task.LeaseToken, task.LeaseExpiresAt, task.EffectiveDate,
                task.Attributes, task.Locations?.Select(location => new ResourceLocationCandidateDto(
                    location.LocationId, location.Url, location.Source, location.Status,
                    location.LastVerifiedAt, location.Artifact)).ToArray(), task.RaceHoldGeneration,
                task.EntryAssignmentFingerprint)));
}
