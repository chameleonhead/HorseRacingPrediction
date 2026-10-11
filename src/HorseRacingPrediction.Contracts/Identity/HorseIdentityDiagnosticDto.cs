using System.Text.Json.Serialization;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Identity;

public sealed record HorseIdentityDiagnosticDto(
    string HorseId,
    [property: JsonConverter(typeof(OffsetPreservingDateTimeOffsetJsonConverter))] DateTimeOffset EventStoreObservedFromUtc,
    [property: JsonConverter(typeof(OffsetPreservingDateTimeOffsetJsonConverter))] DateTimeOffset EventStoreObservedToUtc,
    [property: JsonConverter(typeof(OffsetPreservingDateTimeOffsetJsonConverter))] DateTimeOffset CollectionStoreObservedFromUtc,
    [property: JsonConverter(typeof(OffsetPreservingDateTimeOffsetJsonConverter))] DateTimeOffset CollectionStoreObservedToUtc,
    string CurrentRecordStatus,
    string? CurrentRegisteredName,
    string? CurrentNormalizedName,
    DateOnly? CurrentBirthDate,
    string RegistrationStatus,
    string? InitialRegisteredName,
    string? InitialNormalizedName,
    DateOnly? InitialBirthDate,
    IReadOnlyList<HorseSourceEvidenceDto> Sources,
    IReadOnlyList<HorseIdentityComparisonDto> Comparisons,
    IReadOnlyList<string> EvidenceBlockers,
    bool SourcesTruncated,
    string Coverage);
