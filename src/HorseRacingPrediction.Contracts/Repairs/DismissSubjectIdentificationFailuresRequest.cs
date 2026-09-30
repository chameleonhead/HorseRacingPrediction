
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record DismissSubjectIdentificationFailuresRequest(IReadOnlyList<Guid> NotificationIds);
