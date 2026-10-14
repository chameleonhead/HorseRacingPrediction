using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using EventFlow.Aggregates;
using EventFlow.Core;
using EventFlow.EventStores;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Infrastructure.Persistence.HorseIdentityMigration;

/// <summary>A bounded copy of one persisted EventFlow event row.</summary>
public sealed record TypedHorseIdentityEventRecord(
    long GlobalSequenceNumber,
    string AggregateId,
    string AggregateName,
    int AggregateSequenceNumber,
    string Data,
    string Metadata);

public enum TypedHorseIdentityEventTransformStatus
{
    Unchanged,
    Transformed,
    Rejected,
}

public enum TypedHorseIdentityEventTransformReason
{
    None,
    InvalidMapping,
    InvalidRecord,
    OversizedPayload,
    InvalidJson,
    MetadataMismatch,
    UnsupportedAggregate,
    UnsupportedEventVersion,
    UnsupportedEventType,
    UnsupportedPayloadShape,
    DomainEventConstructionFailure,
    SerializationFailure,
}

public sealed record TypedHorseIdentityEventTransformResult(
    TypedHorseIdentityEventTransformStatus Status,
    TypedHorseIdentityEventTransformReason Reason,
    TypedHorseIdentityEventRecord? Event)
{
    public bool IsAccepted => Status is TypedHorseIdentityEventTransformStatus.Unchanged
        or TypedHorseIdentityEventTransformStatus.Transformed;
}

/// <summary>
/// Produces a transformed EventFlow row without writing it. Only registered, explicitly
/// understood Horse and Race event payloads are accepted; all other aggregates/types fail closed.
/// </summary>
public sealed class TypedHorseIdentityEventTransformer
{
    public const int MaximumMappingCount = 16;
    public const int MaximumSerializedFieldBytes = 64 * 1024;
    public const int MaximumJsonDepth = 16;

    private const int SupportedEventVersion = 1;
    private const string AggregateIdMetadataKey = "aggregate_id";

    private static readonly HashSet<Type> HorseEventsWithoutTypedHorseReferences =
    [
        typeof(HorseRegistered), typeof(HorseProfileUpdated), typeof(HorseAliasMerged),
        typeof(HorseDataCorrected), typeof(HorseJraProfileCollected),
    ];

    private static readonly HashSet<Type> RaceEventsWithoutTypedHorseReferences =
    [
        typeof(RaceCreated), typeof(RaceCardPublished), typeof(RaceWeatherObserved),
        typeof(RaceTrackConditionObserved), typeof(RaceLifecycleStatusChanged), typeof(RaceStarted),
        typeof(PayoutResultDeclared), typeof(RaceDataCorrected), typeof(RaceRescheduled), typeof(RaceClosed),
    ];

    private readonly IEventJsonSerializer _eventJsonSerializer;
    private readonly IDomainEventFactory _domainEventFactory;
    private readonly IReadOnlyDictionary<string, string> _oldToNewHorseIds;

    public TypedHorseIdentityEventTransformer(
        IEventJsonSerializer eventJsonSerializer,
        IDomainEventFactory domainEventFactory,
        IReadOnlyDictionary<string, string> oldToNewHorseIds)
    {
        _eventJsonSerializer = eventJsonSerializer ?? throw new ArgumentNullException(nameof(eventJsonSerializer));
        _domainEventFactory = domainEventFactory ?? throw new ArgumentNullException(nameof(domainEventFactory));
        _oldToNewHorseIds = SnapshotMappings(oldToNewHorseIds);
    }

    public TypedHorseIdentityEventTransformResult Transform(TypedHorseIdentityEventRecord? record)
    {
        if (record is null || record.GlobalSequenceNumber <= 0 || record.AggregateSequenceNumber <= 0
            || string.IsNullOrWhiteSpace(record.AggregateId) || string.IsNullOrWhiteSpace(record.AggregateName)
            || string.IsNullOrEmpty(record.Data) || string.IsNullOrEmpty(record.Metadata))
            return Rejected(TypedHorseIdentityEventTransformReason.InvalidRecord);

        if (!FitsSerializedBound(record.Data) || !FitsSerializedBound(record.Metadata))
            return Rejected(TypedHorseIdentityEventTransformReason.OversizedPayload);

        using var dataJson = ParseBoundedJson(record.Data);
        using var metadataJson = ParseBoundedJson(record.Metadata);
        if (dataJson is null || metadataJson is null)
            return Rejected(TypedHorseIdentityEventTransformReason.InvalidJson);
        if (HasDuplicatePropertyNames(dataJson.RootElement)
            || HasDuplicatePropertyNames(metadataJson.RootElement))
            return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape);

        IDomainEvent domainEvent;
        SerializedEvent canonicalOriginal;
        try
        {
            domainEvent = _eventJsonSerializer.Deserialize(record.Data, record.Metadata);
            canonicalOriginal = _eventJsonSerializer.Serialize(domainEvent);
        }
        catch (Exception)
        {
            return Rejected(TypedHorseIdentityEventTransformReason.InvalidJson);
        }

        if (!HasSupportedMetadata(domainEvent, record))
            return Rejected(TypedHorseIdentityEventTransformReason.MetadataMismatch);
        if (domainEvent.Metadata.EventVersion != SupportedEventVersion)
            return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedEventVersion);

        if (!HasSameJsonValues(record.Data, canonicalOriginal.SerializedData)
            || !HasSameJsonValues(record.Metadata, canonicalOriginal.SerializedMetadata))
            return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape);

        IAggregateEvent aggregateEvent;
        string transformedAggregateId;
        bool payloadChanged;
        try
        {
            if (domainEvent.AggregateType == typeof(HorseAggregate)
                && string.Equals(record.AggregateName, nameof(HorseAggregate), StringComparison.Ordinal))
            {
                if (!IsCanonicalHorseId(record.AggregateId))
                    return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedAggregate);

                transformedAggregateId = MapHorseId(record.AggregateId);
                aggregateEvent = domainEvent.GetAggregateEvent();
                if (!HorseEventsWithoutTypedHorseReferences.Contains(domainEvent.EventType))
                    return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedEventType);

                payloadChanged = false;
            }
            else if (domainEvent.AggregateType == typeof(RaceAggregate)
                && string.Equals(record.AggregateName, nameof(RaceAggregate), StringComparison.Ordinal))
            {
                transformedAggregateId = record.AggregateId;
                (aggregateEvent, payloadChanged) = TransformRaceEvent(
                    domainEvent.GetAggregateEvent(), record.AggregateId);
            }
            else
            {
                return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedAggregate);
            }
        }
        catch (UnsupportedEventException)
        {
            return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedEventType);
        }
        catch (UnsupportedShapeException)
        {
            return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape);
        }

        var aggregateChanged = !string.Equals(transformedAggregateId, record.AggregateId, StringComparison.Ordinal);
        if (!aggregateChanged && !payloadChanged)
            return new(TypedHorseIdentityEventTransformStatus.Unchanged, TypedHorseIdentityEventTransformReason.None, record);

        IDomainEvent transformedDomainEvent;
        try
        {
            var metadataValues = domainEvent.Metadata.ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            metadataValues[AggregateIdMetadataKey] = transformedAggregateId;
            var metadata = new Metadata(metadataValues);
            transformedDomainEvent = _domainEventFactory.Create(
                aggregateEvent, metadata, transformedAggregateId, record.AggregateSequenceNumber);
        }
        catch (Exception)
        {
            return Rejected(TypedHorseIdentityEventTransformReason.DomainEventConstructionFailure);
        }

        SerializedEvent serialized;
        try
        {
            serialized = _eventJsonSerializer.Serialize(transformedDomainEvent);
        }
        catch (Exception)
        {
            return Rejected(TypedHorseIdentityEventTransformReason.SerializationFailure);
        }

        if (!FitsSerializedBound(serialized.SerializedData) || !FitsSerializedBound(serialized.SerializedMetadata))
            return Rejected(TypedHorseIdentityEventTransformReason.OversizedPayload);

        if (!HasSameJsonExceptField(record.Metadata, serialized.SerializedMetadata, AggregateIdMetadataKey)
            || !HasSameJsonExceptIdentityFields(canonicalOriginal.SerializedData, serialized.SerializedData))
            return Rejected(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape);

        if (serialized.AggregateSequenceNumber != record.AggregateSequenceNumber
            || !string.Equals(serialized.Metadata.AggregateId, transformedAggregateId, StringComparison.Ordinal)
            || !string.Equals(serialized.Metadata.AggregateName, record.AggregateName, StringComparison.Ordinal)
            || serialized.Metadata.EventVersion != domainEvent.Metadata.EventVersion
            || !string.Equals(serialized.Metadata.EventName, domainEvent.Metadata.EventName, StringComparison.Ordinal)
            || serialized.Metadata.EventId.GetGuid() != domainEvent.Metadata.EventId.GetGuid()
            || serialized.Metadata.Timestamp != domainEvent.Metadata.Timestamp)
            return Rejected(TypedHorseIdentityEventTransformReason.MetadataMismatch);

        return new(
            TypedHorseIdentityEventTransformStatus.Transformed,
            TypedHorseIdentityEventTransformReason.None,
            record with
            {
                AggregateId = transformedAggregateId,
                Data = serialized.SerializedData,
                Metadata = serialized.SerializedMetadata,
            });
    }

    private (IAggregateEvent Event, bool Changed) TransformRaceEvent(IAggregateEvent value, string raceId)
    {
        switch (value)
        {
            case EntryRegistered e:
                {
                    var horseId = MapHorseId(e.HorseId);
                    var previousHorseId = MapOptionalHorseId(e.PreviousHorseId);
                    var entryId = TransformDerivedEntryId(e.EntryId, raceId, e.HorseId, horseId);
                    var changed = horseId != e.HorseId || previousHorseId != e.PreviousHorseId || entryId != e.EntryId;
                    return changed
                        ? (new EntryRegistered(entryId, horseId, e.HorseNumber, e.JockeyId, e.TrainerId,
                            e.GateNumber, e.AssignedWeight, e.SexCode, e.Age, e.DeclaredWeight, e.DeclaredWeightDiff,
                            e.RunningStyleCode, e.RaceDate, e.RacecourseCode, e.SurfaceCode, e.DistanceMeters,
                            e.DirectionCode, e.GradeCode, e.OwnerName, previousHorseId, e.PreviousJockeyId,
                            e.ParticipationStatus), true)
                        : (e, false);
                }
            case EntryCollectedDataUpdated e:
                {
                    var horseId = MapHorseId(e.HorseId);
                    var entryId = TransformDerivedEntryId(e.EntryId, raceId, e.HorseId, horseId);
                    return horseId != e.HorseId || entryId != e.EntryId
                        ? (new EntryCollectedDataUpdated(entryId, horseId, e.DeclaredWeight, e.DeclaredWeightDiff, e.OwnerName), true)
                        : (e, false);
                }
            case EntryResultDeclared e when e.HorseId is not null:
                {
                    var horseId = MapHorseId(e.HorseId);
                    var entryId = TransformDerivedEntryId(e.EntryId, raceId, e.HorseId, horseId);
                    return horseId != e.HorseId || entryId != e.EntryId
                        ? (new EntryResultDeclared(entryId, e.FinishPosition, e.OfficialTime, e.MarginText,
                            e.LastThreeFurlongTime, e.AbnormalResultCode, e.PrizeMoney, e.CornerPositions,
                            e.Popularity, e.OriginalFinishPosition, e.IsDeadHeat, e.Average1F, horseId, e.JockeyId,
                            e.AdditionalPrizeMoney), true)
                        : (e, false);
                }
            case EntryResultDeclared e:
                return (e, false);
            case RaceResultDeclared e when e.WinningHorseId is not null:
                {
                    var horseId = MapHorseId(e.WinningHorseId);
                    return horseId != e.WinningHorseId
                        ? (new RaceResultDeclared(e.WinningHorseName, e.DeclaredAt, horseId, e.StewardReportText), true)
                        : (e, false);
                }
            case RaceResultDeclared e:
                return (e, false);
            case RaceEntryAssignmentsRepaired e:
                {
                    var previous = TransformEntries(e.PreviousEntries, raceId, out var previousChanged);
                    var current = TransformEntries(e.Entries, raceId, out var currentChanged);
                    return previousChanged || currentChanged
                        ? (new RaceEntryAssignmentsRepaired(e.OperationId, e.Fingerprint, e.SourceUrl, e.ObservedAt,
                            e.SourceEvidenceJson, previous, current, e.RaceDate, e.RacecourseCode, e.GradeCode,
                            e.SurfaceCode, e.DistanceMeters, e.DirectionCode), true)
                        : (e, false);
                }
            case RaceOddsSnapshotRecorded e:
                {
                    var changed = false;
                    var assignments = e.Assignments.Select(assignment =>
                    {
                        var horseId = MapHorseId(assignment.HorseId);
                        var entryId = TransformDerivedEntryId(assignment.EntryId, raceId, assignment.HorseId, horseId);
                        if (horseId == assignment.HorseId && entryId == assignment.EntryId) return assignment;
                        changed = true;
                        return assignment with { HorseId = horseId, EntryId = entryId };
                    }).ToArray();
                    return changed
                        ? (new RaceOddsSnapshotRecorded(e.ObservedAt, e.Entries, e.Observations, assignments), true)
                        : (e, false);
                }
            default:
                if (RaceEventsWithoutTypedHorseReferences.Contains(value.GetType()))
                    return (value, false);
                throw new UnsupportedEventException();
        }
    }

    private IReadOnlyList<EntryDetails> TransformEntries(
        IReadOnlyList<EntryDetails> entries,
        string raceId,
        out bool changed)
    {
        changed = false;
        var transformed = new EntryDetails[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] ?? throw new UnsupportedShapeException();
            var horseId = MapHorseId(entry.HorseId);
            var entryId = TransformDerivedEntryId(entry.EntryId, raceId, entry.HorseId, horseId);
            if (horseId == entry.HorseId && entryId == entry.EntryId)
            {
                transformed[index] = entry;
                continue;
            }

            changed = true;
            transformed[index] = entry with { HorseId = horseId, EntryId = entryId };
        }

        return changed ? Array.AsReadOnly(transformed) : entries;
    }

    private string MapHorseId(string horseId) => _oldToNewHorseIds.TryGetValue(horseId, out var mapped) ? mapped : horseId;

    private string? MapOptionalHorseId(string? horseId) => horseId is null ? null : MapHorseId(horseId);

    private static string TransformDerivedEntryId(string entryId, string raceId, string oldHorseId, string newHorseId) =>
        oldHorseId != newHorseId
        && string.Equals(entryId, DeterministicIdGenerator.BuildRaceEntryId(raceId, oldHorseId), StringComparison.Ordinal)
            ? DeterministicIdGenerator.BuildRaceEntryId(raceId, newHorseId)
            : entryId;

    private static IReadOnlyDictionary<string, string> SnapshotMappings(IReadOnlyDictionary<string, string> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        var destinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (oldId, newId) in mappings)
        {
            if (snapshot.Count >= MaximumMappingCount
                || !IsCanonicalHorseId(oldId)
                || !IsCanonicalHorseId(newId)
                || string.Equals(oldId, newId, StringComparison.Ordinal)
                || !snapshot.TryAdd(oldId, newId)
                || !destinations.Add(newId))
                throw new ArgumentException("The Horse identity map is invalid or exceeds its bound.", nameof(mappings));
        }

        if (snapshot.Count == 0 || snapshot.Keys.Any(destinations.Contains))
            throw new ArgumentException("The Horse identity map must be non-empty and acyclic.", nameof(mappings));

        return new ReadOnlyDictionary<string, string>(snapshot);
    }

    private static bool IsCanonicalHorseId(string? value)
    {
        if (value is null || !value.StartsWith("horse-", StringComparison.Ordinal)
            || !Guid.TryParseExact(value.AsSpan("horse-".Length), "D", out var id))
            return false;

        return string.Equals(value, $"horse-{id:D}", StringComparison.Ordinal);
    }

    private static bool HasSupportedMetadata(IDomainEvent domainEvent, TypedHorseIdentityEventRecord record)
    {
        var metadata = domainEvent.Metadata;
        return domainEvent.AggregateSequenceNumber == record.AggregateSequenceNumber
            && metadata.AggregateSequenceNumber == record.AggregateSequenceNumber
            && string.Equals(domainEvent.GetIdentity().Value, record.AggregateId, StringComparison.Ordinal)
            && string.Equals(metadata.AggregateId, record.AggregateId, StringComparison.Ordinal)
            && string.Equals(metadata.AggregateName, record.AggregateName, StringComparison.Ordinal)
            && string.Equals(metadata.EventName, domainEvent.EventType.Name, StringComparison.Ordinal)
            && metadata.EventVersion == SupportedEventVersion
            && metadata.Timestamp == domainEvent.Timestamp;
    }

    private static bool FitsSerializedBound(string value) =>
        Encoding.UTF8.GetByteCount(value) <= MaximumSerializedFieldBytes;

    private static JsonDocument? ParseBoundedJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasDuplicatePropertyNames(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in value.EnumerateObject())
                        if (!names.Add(property.Name) || HasDuplicatePropertyNames(property.Value)) return true;
                    return false;
                }
            case JsonValueKind.Array:
                return value.EnumerateArray().Any(HasDuplicatePropertyNames);
            default:
                return false;
        }
    }

    private static bool HasSameJsonValues(string candidate, string canonical) =>
        HasSameJson(candidate, canonical, static _ => false);

    private static bool HasSameJsonExceptField(string candidate, string canonical, string allowedField) =>
        HasSameJson(candidate, canonical, name => string.Equals(name, allowedField, StringComparison.OrdinalIgnoreCase));

    private static bool HasSameJsonExceptIdentityFields(string candidate, string canonical) =>
        HasSameJson(candidate, canonical, static name => name is not null
            && (name.Equals("horseId", StringComparison.OrdinalIgnoreCase)
                || name.Equals("previousHorseId", StringComparison.OrdinalIgnoreCase)
                || name.Equals("winningHorseId", StringComparison.OrdinalIgnoreCase)
                || name.Equals("entryId", StringComparison.OrdinalIgnoreCase)));

    private static bool HasSameJson(string candidate, string canonical, Func<string?, bool> mayDiffer)
    {
        using var candidateDocument = ParseBoundedJson(candidate);
        using var canonicalDocument = ParseBoundedJson(canonical);
        if (candidateDocument is null || canonicalDocument is null
            || HasDuplicatePropertyNames(candidateDocument.RootElement)
            || HasDuplicatePropertyNames(canonicalDocument.RootElement))
            return false;
        return SameJson(candidateDocument.RootElement, canonicalDocument.RootElement, mayDiffer, false);
    }

    private static bool SameJson(
        JsonElement candidate,
        JsonElement canonical,
        Func<string?, bool> mayDiffer,
        bool allowDifference)
    {
        if (candidate.ValueKind != canonical.ValueKind)
            return allowDifference;

        switch (candidate.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var candidateProperties = candidate.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                    var canonicalProperties = canonical.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                    if (candidateProperties.Count != canonicalProperties.Count
                        || candidateProperties.Keys.Any(name => !canonicalProperties.ContainsKey(name)))
                        return false;
                    foreach (var (name, candidateValue) in candidateProperties)
                        if (!SameJson(candidateValue, canonicalProperties[name], mayDiffer, mayDiffer(name))) return false;
                    return true;
                }
            case JsonValueKind.Array:
                {
                    var candidateValues = candidate.EnumerateArray().ToArray();
                    var canonicalValues = canonical.EnumerateArray().ToArray();
                    if (candidateValues.Length != canonicalValues.Length) return false;
                    for (var index = 0; index < candidateValues.Length; index++)
                        if (!SameJson(candidateValues[index], canonicalValues[index], mayDiffer, false)) return false;
                    return true;
                }
            default:
                return allowDifference || JsonElement.DeepEquals(candidate, canonical);
        }
    }

    private static TypedHorseIdentityEventTransformResult Rejected(TypedHorseIdentityEventTransformReason reason) =>
        new(TypedHorseIdentityEventTransformStatus.Rejected, reason, null);

    private sealed class UnsupportedEventException : Exception;
    private sealed class UnsupportedShapeException : Exception;
}
