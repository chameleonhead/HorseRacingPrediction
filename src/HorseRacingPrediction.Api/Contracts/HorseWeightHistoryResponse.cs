namespace HorseRacingPrediction.Api.Contracts;

public sealed record HorseWeightHistoryResponse(
    string HorseId,
    IReadOnlyList<HorseWeightEntryResponse> WeightHistory);

public sealed record HorseWeightEntryResponse(
    string RaceId,
    string EntryId,
    DateTimeOffset RecordedAt,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff);
