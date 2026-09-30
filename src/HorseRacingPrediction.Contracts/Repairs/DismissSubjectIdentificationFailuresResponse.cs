
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record DismissSubjectIdentificationFailuresResponse(
    int SelectedCount, int DismissedCount, int AlreadyClosedCount);
