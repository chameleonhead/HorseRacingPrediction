namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionDispatchCompatibilityKeyDto(string Provider, CollectionDefinitionIdDto Definition,
    DateOnly? EffectiveDate, CollectionLane Lane,
    CollectionDispatchGroupKind GroupKind = CollectionDispatchGroupKind.Definition, string? GroupKey = null)
{
    public bool IsSupported() => !string.IsNullOrWhiteSpace(Provider)
        && (GroupKind == CollectionDispatchGroupKind.Definition
            ? !string.IsNullOrWhiteSpace(Definition.Value) : !string.IsNullOrWhiteSpace(GroupKey));
}
