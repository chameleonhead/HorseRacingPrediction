namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionTaskSubmissionDto(string Mode, CollectionRequestReceiptDto Receipt,
    CollectionResourceKeyDto Resource, CollectionDefinitionIdDto Definition, DateOnly? EffectiveDate,
    string? ExplicitUrl, IReadOnlyDictionary<string, string> Attributes);
