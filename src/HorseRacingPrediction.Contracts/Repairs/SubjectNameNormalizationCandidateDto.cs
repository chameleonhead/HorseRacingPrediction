
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectNameNormalizationCandidateDto(
    CollectionResourceType SubjectType,
    string SubjectId,
    string CurrentDisplayName,
    string CurrentNormalizedName,
    string ProposedDisplayName,
    string ProposedNormalizedName,
    bool HasChanges,
    bool CanApply,
    string Evaluation,
    string? BlockingReason,
    IReadOnlyList<string> ConflictingSubjectIds,
    string ManifestToken);
