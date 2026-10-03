namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record ApplySubjectIdentificationCandidateRequest(
    Guid NotificationId,
    SubjectIdentificationCandidateSelectionInputDto? Selection);
