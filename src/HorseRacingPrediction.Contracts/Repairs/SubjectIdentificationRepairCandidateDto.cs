
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectIdentificationRepairCandidateDto(
    Guid NotificationId,
    Guid TaskId,
    CollectionResourceType SubjectType,
    string SubjectId,
    string DefinitionId,
    string? ErrorMessage,
    DateTimeOffset FailedAt,
    string Evaluation,
    bool SafeToExecute,
    string? BlockingReason,
    string? SuggestedUrl,
    string? HorseMergeCandidateId = null,
    string? MergeTargetId = null);
