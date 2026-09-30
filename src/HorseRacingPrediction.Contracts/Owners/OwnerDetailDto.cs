
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Owners;

public sealed record OwnerDetailDto(
    OwnerSummaryDto Summary,
    IReadOnlyList<RelatedObjectDto> CurrentHorses,
    IReadOnlyList<RelatedObjectDto> RelatedTrainers,
    IReadOnlyList<ParticipationHistoryEntryDto> Participations,
    IReadOnlyList<OwnerMergeAuditDto> MergeHistory,
    bool HasMoreParticipations = false,
    IReadOnlyList<RelationshipSummaryDto>? TopHorses = null);
