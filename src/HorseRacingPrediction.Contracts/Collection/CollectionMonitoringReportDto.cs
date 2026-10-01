namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionMonitoringReportDto(DateTimeOffset Cutoff, DateTimeOffset CompletedAt,
    bool Enabled, bool ChangeRecordEnabled, bool Suppressed, string? SuppressionReason, bool Truncated,
    IReadOnlyList<CollectionOperationalFindingDto> Findings,
    CollectionMonitoringOutcomeDto Outcome = CollectionMonitoringOutcomeDto.Healthy,
    IReadOnlyList<CollectionDefinitionFlowDiagnosticDto>? DefinitionFlows = null);
