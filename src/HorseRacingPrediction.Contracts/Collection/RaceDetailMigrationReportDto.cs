namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RaceDetailMigrationReportDto(bool DryRun, int SourceResources, int TargetResources,
    int Requests, int Tasks, int Attempts, int Locations, int States, int SupplementRequests,
    IReadOnlyList<string> Errors);
