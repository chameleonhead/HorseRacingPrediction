
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record ApplyHorseIdentityRepairResponse(
    string RepairId, int AppliedCount, int SkippedCount, int DisabledCollectionTaskCount = 0,
    int RunningCancellationRequestCount = 0);
