
namespace HorseRacingPrediction.Contracts.Owners;

public sealed record RelatedObjectDto(string ObjectType, string ObjectId, string DisplayName, int RelationshipCount);
