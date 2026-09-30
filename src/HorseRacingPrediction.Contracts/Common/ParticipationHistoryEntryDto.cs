
namespace HorseRacingPrediction.Contracts.Common;

public sealed record ParticipationHistoryEntryDto(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    string HorseId,
    string HorseName,
    string? JockeyId,
    string? JockeyName,
    string? TrainerId,
    string? TrainerName,
    string? OwnerName,
    int? FinishPosition,
    decimal? PrizeMoney);
