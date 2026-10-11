using System.Data;
using System.Globalization;
using System.Text.Json;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HorseRacingPrediction.Api;

internal sealed class HorseIdentityDiagnosticService(
    IDbContextProvider<EventStoreDbContext> eventStoreProvider,
    CollectionPlatformStore collectionStore)
{
    private const int MaxPayloadBytes = 64 * 1024;
    private const int RegistrationRowLimit = 2;
    private static readonly SemaphoreSlim Admission = new(1, 1);

    public async Task<HorseIdentityDiagnosticDto?> GetAsync(string horseId, CancellationToken requestToken)
    {
        if (!IsCanonicalHorseId(horseId)) throw new InvalidHorseIdentityDiagnosticIdException();
        if (!await Admission.WaitAsync(0, requestToken).ConfigureAwait(false))
            throw new HorseIdentityDiagnosticBusyException();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var eventStoreStarted = DateTimeOffset.UtcNow;
            var (current, profile, registration) = await ReadEventStoreAsync(horseId, deadline.Token).ConfigureAwait(false);
            var eventStoreFinished = DateTimeOffset.UtcNow;

            var collectionStoreStarted = DateTimeOffset.UtcNow;
            var collection = await collectionStore.GetHorseIdentitySourcesReadOnlyAsync(horseId, deadline.Token)
                .ConfigureAwait(false);
            var collectionStoreFinished = DateTimeOffset.UtcNow;

            if (current is null && registration is null) return null;
            var sources = BuildSources(profile, collection);
            var comparisons = BuildComparisons(horseId, registration, sources);

            var blockers = new List<string>();
            if (registration?.Status.StartsWith("Unsupported:", StringComparison.Ordinal) == true)
                blockers.Add(registration.Status["Unsupported:".Length..]);
            if (current?.Status.StartsWith("Unsupported:", StringComparison.Ordinal) == true)
                blockers.Add(current.Status["Unsupported:".Length..]);
            if (collection.Truncated) blockers.Add("CollectionSourcesTruncated");
            if (registration?.Status == "MissingNormalizedName")
                blockers.Add("MissingNormalizedName");
            if (profile?.Status != "Read" && profile is not null)
                blockers.Add($"Profile{profile.Status}");
            blockers.AddRange(collection.Rows.Where(row => row.Status != "Read")
                .Select(row => $"Collection{row.Status}").Distinct(StringComparer.Ordinal));
            if (sources.Any(source => source.Status is "Invalid" or "Conflicting"
                || source.SourceUrlStatus == "Invalid"))
                blockers.Add("InvalidSourceEvidence");

            deadline.Token.ThrowIfCancellationRequested();
            return new HorseIdentityDiagnosticDto(
                horseId,
                eventStoreStarted,
                eventStoreFinished,
                collectionStoreStarted,
                collectionStoreFinished,
                current is null ? "Missing" : current.Status.StartsWith("Unsupported:", StringComparison.Ordinal)
                    ? "Unsupported" : "Supported",
                current?.RegisteredName,
                current?.NormalizedName,
                current?.BirthDate,
                registration is null ? "Missing" : registration.Status.StartsWith("Unsupported:", StringComparison.Ordinal)
                    ? "Unsupported" : registration.Status == "MissingNormalizedName" ? "Missing" : "Supported",
                registration?.RegisteredName,
                registration?.NormalizedName,
                registration?.BirthDate,
                sources,
                comparisons,
                blockers,
                collection.Truncated,
                "PointInTime; EventStore and CollectionStore were read in separate transactions; event sequence 1, current horse/profile projections, and bounded JRA horse resource metadata only.");
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            throw new HorseIdentityDiagnosticDeadlineException();
        }
        catch (SqliteException) when (requestToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(requestToken);
        }
        catch (SqliteException) when (!requestToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new HorseIdentityDiagnosticDeadlineException();
        }
        finally
        {
            Admission.Release();
        }
    }

    private async Task<(CurrentHorse? Current, CurrentProfile? Profile, RegistrationEvidence? Registration)> ReadEventStoreAsync(
        string horseId, CancellationToken cancellationToken)
    {
        string connectionString;
        using (var configuredContext = eventStoreProvider.CreateContext())
            connectionString = configuredContext.Database.GetConnectionString()
                ?? throw new InvalidOperationException("Event store connection is unavailable.");

        var connectionOptions = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        };
        await using var connection = new SqliteConnection(connectionOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA query_only = ON;";
            pragma.CommandTimeout = 1;
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var options = new DbContextOptionsBuilder<EventStoreDbContext>().UseSqlite(connection).Options;
        cancellationToken.ThrowIfCancellationRequested();
        await using var sqliteTransaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
        await using var db = new EventStoreDbContext(options);
        await db.Database.UseTransactionAsync(sqliteTransaction, cancellationToken).ConfigureAwait(false);

        var databaseConnection = db.Database.GetDbConnection();
        var transactionHandle = db.Database.CurrentTransaction!.GetDbTransaction();
        var current = await ReadCurrentHorseAsync(databaseConnection, transactionHandle, horseId, cancellationToken)
            .ConfigureAwait(false);
        var profile = await ReadCurrentProfileAsync(databaseConnection, transactionHandle, horseId, cancellationToken)
            .ConfigureAwait(false);
        var registration = await ReadRegistrationAsync(db, horseId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (current, profile, registration);
    }

    private static async Task<CurrentHorse?> ReadCurrentHorseAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, string horseId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE WHEN length(CAST(RegisteredName AS BLOB)) <= $maxBytes THEN RegisteredName ELSE NULL END,
                   length(CAST(RegisteredName AS BLOB)),
                   CASE WHEN length(CAST(NormalizedName AS BLOB)) <= $maxBytes THEN NormalizedName ELSE NULL END,
                   length(CAST(NormalizedName AS BLOB)),
                   BirthDate
            FROM Horses WHERE HorseId = $horseId LIMIT 1;
            """;
        command.Parameters.Add(new SqliteParameter("$maxBytes", MaxPayloadBytes));
        command.Parameters.Add(new SqliteParameter("$horseId", horseId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.GetInt64(1) > MaxPayloadBytes || reader.GetInt64(3) > MaxPayloadBytes)
            return new("Unsupported:CurrentProjectionTooLarge", null, null, null);

        var registeredName = reader.IsDBNull(0) ? null : reader.GetString(0);
        var normalizedName = reader.IsDBNull(2) ? null : reader.GetString(2);
        if (registeredName is null || normalizedName is null)
            return new("Unsupported:CurrentProjectionShape", null, null, null);
        DateOnly? birthDate = null;
        if (!reader.IsDBNull(4))
        {
            var storedBirthDate = reader.GetString(4);
            if (!DateOnly.TryParseExact(storedBirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                return new("Unsupported:InvalidCurrentBirthDate", null, null, null);
            birthDate = parsed;
        }
        return new("Found", registeredName, normalizedName, birthDate);
    }

    private static async Task<CurrentProfile?> ReadCurrentProfileAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, string horseId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE WHEN length(CAST(SourceIdentity AS BLOB)) <= $maxBytes THEN SourceIdentity ELSE NULL END,
                   length(CAST(SourceIdentity AS BLOB)),
                   CASE WHEN length(CAST(SourceUrl AS BLOB)) <= $maxBytes THEN SourceUrl ELSE NULL END,
                   length(CAST(SourceUrl AS BLOB))
            FROM JraSubjectProfileReadModel WHERE SubjectId = $horseId LIMIT 1;
            """;
        command.Parameters.Add(new SqliteParameter("$maxBytes", MaxPayloadBytes));
        command.Parameters.Add(new SqliteParameter("$horseId", horseId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.GetInt64(1) > MaxPayloadBytes || reader.GetInt64(3) > MaxPayloadBytes)
            return new(null, null, "PayloadTooLarge");
        return new(reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(2) ? null : reader.GetString(2), "Read");
    }

    private static async Task<RegistrationEvidence?> ReadRegistrationAsync(EventStoreDbContext db, string horseId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT AggregateName,
                   CASE WHEN length(CAST(Data AS BLOB)) <= $maxBytes THEN Data ELSE NULL END,
                   length(CAST(Data AS BLOB)),
                   CASE WHEN length(CAST(Metadata AS BLOB)) <= $maxBytes THEN Metadata ELSE NULL END,
                   length(CAST(Metadata AS BLOB))
            FROM EventEntity
            WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1
            ORDER BY GlobalSequenceNumber
            LIMIT $limit;
            """;
        command.Parameters.Add(new SqliteParameter("$maxBytes", MaxPayloadBytes));
        command.Parameters.Add(new SqliteParameter("$horseId", horseId));
        command.Parameters.Add(new SqliteParameter("$limit", RegistrationRowLimit));

        var rows = new List<RawRegistration>(RegistrationRowLimit);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new RawRegistration(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt64(4)));
            }
        }

        if (rows.Count == 0) return null;
        if (rows.Count > 1) return RegistrationEvidence.Unsupported("DuplicateSequenceOne");
        var row = rows[0];
        if (!string.Equals(row.AggregateName, nameof(HorseAggregate), StringComparison.Ordinal))
            return RegistrationEvidence.Unsupported("UnexpectedAggregateType");
        if (row.DataBytes > MaxPayloadBytes || row.MetadataBytes > MaxPayloadBytes)
            return RegistrationEvidence.Unsupported("PayloadTooLarge");
        if (row.Data is null || row.Metadata is null)
            return RegistrationEvidence.Unsupported("PayloadUnavailable");
        var metadataBlocker = ValidateRegisteredEventMetadata(row.Metadata);
        if (metadataBlocker is not null) return RegistrationEvidence.Unsupported(metadataBlocker);

        try
        {
            using var data = JsonDocument.Parse(row.Data, new JsonDocumentOptions { MaxDepth = 16 });
            if (data.RootElement.ValueKind != JsonValueKind.Object)
                return RegistrationEvidence.Unsupported("UnexpectedEventShape");

            var fieldBlocker = ValidateRegisteredEventShape(data.RootElement);
            if (fieldBlocker is not null) return RegistrationEvidence.Unsupported(fieldBlocker);
            var registeredName = GetString(data.RootElement, "RegisteredName")!;
            var normalizedName = GetString(data.RootElement, "NormalizedName");
            var birthDate = ReadDateOnly(data.RootElement, "BirthDate", out var invalidBirthDate);
            if (invalidBirthDate) return RegistrationEvidence.Unsupported("InvalidInitialBirthDate");
            return new(normalizedName is null ? "MissingNormalizedName" : "Supported",
                registeredName, normalizedName, birthDate);
        }
        catch (JsonException)
        {
            return RegistrationEvidence.Unsupported("InvalidEventJson");
        }
    }

    private static string? ValidateRegisteredEventMetadata(string metadataJson)
    {
        try
        {
            using var metadata = JsonDocument.Parse(metadataJson, new JsonDocumentOptions { MaxDepth = 16 });
            if (metadata.RootElement.ValueKind != JsonValueKind.Object) return "InvalidMetadataShape";
            if (metadata.RootElement.EnumerateObject().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1)) return "DuplicateMetadataField";
            var eventName = GetString(metadata.RootElement, "event_name");
            if (eventName is null) return "MissingEventMetadata";
            if (!string.Equals(eventName, nameof(HorseRegistered), StringComparison.Ordinal))
                return "UnexpectedEventType";
            var aggregateName = GetString(metadata.RootElement, "aggregate_name");
            if (!string.Equals(aggregateName, nameof(HorseAggregate), StringComparison.Ordinal))
                return "UnexpectedAggregateMetadata";
            if (!TryGetEventVersion(metadata.RootElement, out var eventVersion)) return "MissingEventVersion";
            return eventVersion == 1 ? null : "UnsupportedEventVersion";
        }
        catch (JsonException)
        {
            return "InvalidMetadataJson";
        }
    }

    private static bool TryGetEventVersion(JsonElement metadata, out int version)
    {
        version = 0;
        foreach (var property in metadata.EnumerateObject())
        {
            if (!property.Name.Equals("event_version", StringComparison.OrdinalIgnoreCase)) continue;
            if (property.Value.ValueKind == JsonValueKind.Number)
                return property.Value.TryGetInt32(out version);
            return property.Value.ValueKind == JsonValueKind.String
                && int.TryParse(property.Value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out version);
        }
        return false;
    }

    private static string? ValidateRegisteredEventShape(JsonElement data)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RegisteredName", "NormalizedName", "SexCode", "BirthDate", "OwnerName", "BreederName",
            "SireName", "DamName", "DamsireName", "CoatColor",
        };
        var properties = data.EnumerateObject().ToArray();
        if (properties.GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            return "DuplicateEventField";
        if (properties.Any(property => !expected.Contains(property.Name))) return "UnknownEventField";
        if (properties.Length != expected.Count || expected.Any(name => !properties.Any(property =>
                property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))) return "MissingEventField";
        foreach (var property in properties)
        {
            if (property.Name.Equals("RegisteredName", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.String) return "InvalidEventFieldType";
            }
            else if (property.Name.Equals("NormalizedName", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return "InvalidEventFieldType";
            }
            else if (property.Name.Equals("BirthDate", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return "InvalidEventFieldType";
            }
            else if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return "InvalidEventFieldType";
        }
        return null;
    }

    private static IReadOnlyList<HorseSourceEvidenceDto> BuildSources(CurrentProfile? profile,
        CollectionHorseIdentitySources collection)
    {
        var sources = new List<HorseSourceEvidenceDto>(collection.Rows.Count + 1);
        if (profile is not null)
            sources.Add(profile.Status == "Read"
                ? CreateSource("ProfileReadModel", profile.SourceIdentity, profile.SourceUrl, profile.Status)
                : new("ProfileReadModel", MapStatus(profile.Status), null, null, MapStatus(profile.Status), null));
        else
            sources.Add(new("ProfileReadModel", "Missing", null, null, "Missing", null));

        foreach (var row in collection.Rows)
        {
            if (row.Status != "Read")
            {
                sources.Add(new("CollectionResource", MapStatus(row.Status), null, null, MapStatus(row.Status), null));
                continue;
            }
            sources.Add(CreateSource("CollectionResource", row.SourceIdentity, row.SourceUrl, "Read"));
        }

        if (collection.Truncated)
            sources.Add(new("CollectionResource", "Truncated", null, null, "Truncated", null));
        return sources;
    }

    private static HorseSourceEvidenceDto CreateSource(string origin, string? sourceIdentity, string? sourceUrl, string rowStatus)
    {
        var identity = NormalizeSourceIdentity(sourceIdentity);
        var urlIdentity = NormalizeSourceUrl(sourceUrl);
        var status = identity.Status == "Missing"
            ? rowStatus switch
            {
                "Missing" => "Missing",
                "Invalid" => "Invalid",
                "Unsupported" => "Unsupported",
                "Truncated" => "Truncated",
                _ => "Missing",
            }
            : MapStatus(identity.Status);
        if (identity.Status == "Valid" && urlIdentity.Status == "Valid"
            && !string.Equals(identity.Value, urlIdentity.Value, StringComparison.Ordinal))
            status = "Conflicting";
        return new(origin,
            status,
            identity.Value,
            identity.Value,
            sourceUrl is null ? "Missing" : MapStatus(urlIdentity.Status),
            urlIdentity.Value);
    }

    private static (string Status, string? Value) NormalizeSourceIdentity(string? value)
    {
        if (value is null) return ("Missing", null);
        if (value.Length == 0 || string.IsNullOrWhiteSpace(value)) return ("Invalid", null);
        if (IsCanonicalJraHorseIdentity(value)) return ("Valid", value);
        return NormalizeSourceUrl(value);
    }

    private static (string Status, string? Value) NormalizeSourceUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ("Missing", null);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return ("Invalid", null);
        if (!JraSourceIdentity.TryNormalizeHorse(value, out var normalized)
            || !IsCanonicalJraHorseIdentity(normalized))
            return ("Invalid", null);
        return ("Valid", normalized);
    }

    private static bool IsCanonicalJraHorseIdentity(string value)
    {
        if (value.Length != "jra-horse:".Length + 10
            || !value.StartsWith("jra-horse:", StringComparison.Ordinal)) return false;
        return value.AsSpan("jra-horse:".Length).ToString().All(static c => c is >= '0' and <= '9');
    }

    private static IReadOnlyList<HorseIdentityComparisonDto> BuildComparisons(string horseId,
        RegistrationEvidence? registration, IReadOnlyList<HorseSourceEvidenceDto> sources)
    {
        var comparisons = new List<HorseIdentityComparisonDto>();
        if (registration?.RegisteredName is { } registeredName)
        {
            var legacyInput = registration.NormalizedName ?? registration.RegisteredName;
            var legacyEvidence = registration.NormalizedName is null
                ? "Missing"
                : string.IsNullOrWhiteSpace(registration.NormalizedName) ? "Invalid" : "Supported";
            var legacyId = DeterministicIdGenerator.BuildEntityId("horse",
                DeterministicIdGenerator.NormalizeKey(legacyInput.Trim()));
            comparisons.Add(new("LegacyNormalizedName",
                registration.NormalizedName is null ? "InitialRegisteredNameFallback" : "InitialNormalizedName",
                legacyEvidence, legacyId, legacyId == horseId ? "Matches" : "Differs"));

            var currentNameId = DeterministicIdGenerator.BuildHorseId(registeredName);
            comparisons.Add(new("CurrentNameOnly", "InitialRegisteredName", "Supported", currentNameId,
                currentNameId == horseId ? "Matches" : "Differs"));
        }

        foreach (var source in sources.Where(x => x.NormalizedSourceIdentity is not null))
        {
            var sourceId = DeterministicIdGenerator.BuildEntityId("horse",
                $"JRA|{source.NormalizedSourceIdentity}");
            comparisons.Add(new($"Source:{source.Origin}", "SourceIdentity", "Supported", sourceId,
                sourceId == horseId ? "Matches" : "Differs"));
        }
        foreach (var source in sources.Where(x => x.SourceUrlHorseIdentity is not null))
        {
            var sourceId = DeterministicIdGenerator.BuildEntityId("horse",
                $"JRA|{source.SourceUrlHorseIdentity}");
            comparisons.Add(new($"SourceUrl:{source.Origin}", "SourceUrlHorseIdentity", source.Status == "Conflicting" ? "Conflicting" : "Supported", sourceId,
                sourceId == horseId ? "Matches" : "Differs"));
        }
        return comparisons;
    }

    private static string MapStatus(string status) => status switch
    {
        "Read" or "Found" or "Valid" or "Supported" => "Supported",
        "Missing" => "Missing",
        "Invalid" or "InvalidAttributes" => "Invalid",
        "Truncated" => "Truncated",
        _ => "Unsupported",
    };

    private static string? GetString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        return null;
    }

    private static DateOnly? ReadDateOnly(JsonElement element, string propertyName, out bool invalid)
    {
        invalid = false;
        var value = GetString(element, propertyName);
        if (value is null) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            invalid = true;
            return null;
        }
        return date;
    }

    internal static bool IsCanonicalHorseId(string? horseId)
    {
        if (horseId is null || horseId.Length != 6 + 36 || !horseId.StartsWith("horse-", StringComparison.Ordinal)) return false;
        return Guid.TryParseExact(horseId.AsSpan(6), "D", out var id)
            && string.Equals(id.ToString("D"), horseId[6..], StringComparison.Ordinal);
    }

    private sealed record CurrentHorse(string Status, string? RegisteredName, string? NormalizedName, DateOnly? BirthDate);
    private sealed record CurrentProfile(string? SourceIdentity, string? SourceUrl, string Status);
    private sealed record RawRegistration(string AggregateName, string? Data, long DataBytes, string? Metadata, long MetadataBytes);
    private sealed record RegistrationEvidence(string Status, string? RegisteredName, string? NormalizedName, DateOnly? BirthDate)
    {
        public static RegistrationEvidence Unsupported(string reason) => new($"Unsupported:{reason}", null, null, null);
    }
}

internal sealed class InvalidHorseIdentityDiagnosticIdException : Exception { }
internal sealed class HorseIdentityDiagnosticBusyException : Exception { }
internal sealed class HorseIdentityDiagnosticDeadlineException : Exception { }
