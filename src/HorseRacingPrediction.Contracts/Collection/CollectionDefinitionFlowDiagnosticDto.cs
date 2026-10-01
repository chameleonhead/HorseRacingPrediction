namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionDefinitionFlowDiagnosticDto(string Definition, string Lane,
    string CompatibilityKey, int Arrived, int Dispatched, int Completed, int Active,
    double OldestAgeMinutes, string Classification);
