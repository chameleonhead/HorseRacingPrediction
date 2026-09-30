namespace HorseRacingPrediction.Contracts;

public sealed record ParticipationHistoryDto(
    string SubjectType,
    string SubjectId,
    IReadOnlyList<ParticipationHistoryEntryDto> Entries,
    IReadOnlyList<RelationshipSummaryDto> Relationships,
    bool HasMore = false);

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

public sealed record RelationshipSummaryDto(
    string ObjectType,
    string ObjectId,
    string DisplayName,
    string RelationshipName,
    int ParticipationCount,
    DateOnly? LastParticipationDate,
    decimal PrizeMoneyTotal = 0m,
    int FirstPlaceCount = 0);
