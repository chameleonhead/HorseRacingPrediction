namespace HorseRacingPrediction.Contracts.Collection;

public sealed record OwnerIdentityMigrationSampleDto(string DisplayName, string CanonicalId, string LegacyId,
    bool HasAliasMapping);
