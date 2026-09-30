
namespace HorseRacingPrediction.Contracts.Owners;

public sealed record OwnerSummaryDto(
    string OwnerId,
    string DisplayName,
    IReadOnlyList<string> NameVariants,
    int CurrentHorseCount,
    int ParticipationCount,
    DateOnly? LastParticipationDate);
