using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record SubjectNameNormalizationCandidate(
    ResourceType SubjectType,
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

public sealed record SubjectNameNormalizationPage(
    IReadOnlyList<SubjectNameNormalizationCandidate> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record ApplySubjectNameNormalizationRequest(
    IReadOnlyList<ApplySubjectNameNormalizationItem> Items);

public sealed record ApplySubjectNameNormalizationItem(
    ResourceType SubjectType,
    string SubjectId,
    string ManifestToken);

public sealed record SubjectNameNormalizationApplyResult(
    int SelectedCount,
    int AppliedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<SubjectNameNormalizationItemResult> Items);

public sealed record SubjectNameNormalizationItemResult(
    ResourceType SubjectType,
    string SubjectId,
    string Status,
    string Message);
