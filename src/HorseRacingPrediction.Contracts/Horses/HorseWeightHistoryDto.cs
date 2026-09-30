
namespace HorseRacingPrediction.Contracts.Horses;

public sealed record HorseWeightHistoryDto(
    string HorseId,
    IReadOnlyList<HorseWeightEntryDto> WeightHistory);
