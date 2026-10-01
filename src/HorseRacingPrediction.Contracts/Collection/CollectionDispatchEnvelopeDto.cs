namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionDispatchEnvelopeDto(Guid EnvelopeId,
    CollectionDispatchCompatibilityKeyDto Compatibility,
    IReadOnlyList<CollectionDispatchTaskReferenceDto> Tasks, int ContractVersion = 2)
{
    public const int CurrentContractVersion = 2;
    public bool IsSupported() => ContractVersion is 1 or CurrentContractVersion
        && EnvelopeId != Guid.Empty && Compatibility is not null && Compatibility.IsSupported()
        && (ContractVersion != 1 || Compatibility.GroupKind == CollectionDispatchGroupKind.Definition)
        && Tasks is { Count: > 0 } && Tasks.All(x => x is not null && x.IsSupported())
        && Tasks.Select(x => x.TaskId).Distinct().Count() == Tasks.Count;
}
