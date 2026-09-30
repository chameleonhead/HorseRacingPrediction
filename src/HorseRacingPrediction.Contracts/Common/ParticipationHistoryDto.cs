
namespace HorseRacingPrediction.Contracts.Common;

public sealed record ParticipationHistoryDto(
    string SubjectType,
    string SubjectId,
    IReadOnlyList<ParticipationHistoryEntryDto> Entries,
    IReadOnlyList<RelationshipSummaryDto> Relationships,
    bool HasMore = false);
