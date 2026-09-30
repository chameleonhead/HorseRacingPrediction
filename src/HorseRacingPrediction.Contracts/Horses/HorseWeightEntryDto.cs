
namespace HorseRacingPrediction.Contracts.Horses;

public sealed record HorseWeightEntryDto(
    string RaceId,
    string EntryId,
    DateTimeOffset RecordedAt,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff);
