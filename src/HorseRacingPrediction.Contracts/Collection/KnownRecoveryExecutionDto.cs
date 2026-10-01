namespace HorseRacingPrediction.Contracts.Collection;

public sealed record KnownRecoveryExecutionDto(string RecipeId, string BackupId, string BackupFileName,
    int Examined, int Recovered, int Reused, int Suppressed, int Skipped, int Failed);
