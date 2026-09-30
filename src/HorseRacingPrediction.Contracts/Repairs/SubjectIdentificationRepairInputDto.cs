
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectIdentificationRepairInputDto(Guid NotificationId, string? CorrectionUrl = null);
