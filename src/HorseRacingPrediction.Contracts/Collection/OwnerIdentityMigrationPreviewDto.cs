namespace HorseRacingPrediction.Contracts.Collection;

public sealed record OwnerIdentityMigrationPreviewDto(int DistinctOwnerNames, int CanonicalIds,
    int LegacyIds, int AliasMappedNames, IReadOnlyList<OwnerIdentityMigrationSampleDto> Samples);
