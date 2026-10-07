namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRuntimeStatusDto(
    Guid InstanceId,
    DateTimeOffset InstanceStartedAtUtc,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CollectionRuntimeActionStatusDto> Actions);
