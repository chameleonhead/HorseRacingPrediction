namespace HorseRacingPrediction.Contracts;

public sealed record HorseWeightHistoryDto(
    string HorseId,
    IReadOnlyList<HorseWeightEntryDto> WeightHistory);

public sealed record HorseWeightEntryDto(
    string RaceId,
    string EntryId,
    DateTimeOffset RecordedAt,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff);
