using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectIdentificationCandidateApplicationDto(
    Guid NotificationId,
    string SelectedUrl,
    string SelectedName,
    CollectionResourceKeyDto CanonicalResource,
    CollectionDefinitionIdDto CanonicalDefinition,
    Guid CanonicalTaskId,
    string? Selector,
    DateTimeOffset SelectedAt,
    bool CreatedTask,
    bool ReusedTask);
