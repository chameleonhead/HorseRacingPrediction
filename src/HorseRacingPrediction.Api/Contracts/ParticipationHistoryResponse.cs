namespace HorseRacingPrediction.Api.Contracts;

public sealed record ParticipationHistoryResponse(
    string SubjectType,
    string SubjectId,
    IReadOnlyList<ParticipationHistoryEntryResponse> Entries,
    IReadOnlyList<RelationshipSummaryResponse> Relationships);

public sealed record ParticipationHistoryEntryResponse(
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

public sealed record RelationshipSummaryResponse(
    string ObjectType,
    string ObjectId,
    string DisplayName,
    string RelationshipName,
    int ParticipationCount,
    DateOnly? LastParticipationDate);
