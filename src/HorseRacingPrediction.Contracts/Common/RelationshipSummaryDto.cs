
namespace HorseRacingPrediction.Contracts.Common;

public sealed record RelationshipSummaryDto(
    string ObjectType,
    string ObjectId,
    string DisplayName,
    string RelationshipName,
    int ParticipationCount,
    DateOnly? LastParticipationDate,
    decimal PrizeMoneyTotal = 0m,
    int FirstPlaceCount = 0);
