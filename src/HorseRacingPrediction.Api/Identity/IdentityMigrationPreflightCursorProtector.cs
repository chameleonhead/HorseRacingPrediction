using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Identity;

internal sealed class IdentityMigrationPreflightCursorProtector
{
    private const string Purpose = "HorseRacingPrediction.IdentityMigrationPreflight.Cursor.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = IdentityMigrationPreflightPolicy.MaximumJsonDepth,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    internal IdentityMigrationPreflightCursorProtector(IDataProtectionProvider provider, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _timeProvider = timeProvider;
    }

    internal string Protect(IdentityMigrationPreflightCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ValidateForProtection(cursor);

        var now = _timeProvider.GetUtcNow();
        var payload = cursor with
        {
            PolicyVersion = IdentityMigrationPreflightPolicy.PolicyVersion,
            StoreSelection = IdentityMigrationPreflightPolicy.StoreSelection,
            SchemaAssumptionVersion = IdentityMigrationPreflightPolicy.SchemaAssumptionVersion,
            ExpiresAtUtc = now.Add(IdentityMigrationPreflightPolicy.CursorLifetime),
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > IdentityMigrationPreflightPolicy.MaximumCursorPayloadBytes)
            throw new ArgumentException("Cursor state exceeds the bounded payload size.", nameof(cursor));

        var token = _protector.Protect(json, IdentityMigrationPreflightPolicy.CursorLifetime);
        if (token.Length > IdentityMigrationPreflightPolicy.MaximumCursorCharacters)
            throw new ArgumentException("Protected cursor exceeds the bounded token size.", nameof(cursor));
        return token;
    }

    internal bool TryUnprotect(string? token, string expectedSchemaAssumptions,
        out IdentityMigrationPreflightCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(expectedSchemaAssumptions)
            || expectedSchemaAssumptions.Length > IdentityMigrationPreflightPolicy.MaximumSchemaAssumptionsCharacters)
            return false;

        if (!TryUnprotectAuthenticated(token, out var authenticated)
            || authenticated is null
            || !string.Equals(authenticated.SchemaAssumptions, expectedSchemaAssumptions, StringComparison.Ordinal))
            return false;

        cursor = authenticated;
        return true;
    }

    internal bool TryUnprotectAuthenticated(string? token, out IdentityMigrationPreflightCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > IdentityMigrationPreflightPolicy.MaximumCursorCharacters)
            return false;

        try
        {
            var json = _protector.Unprotect(token, out var protectorExpiration);
            if (Encoding.UTF8.GetByteCount(json) > IdentityMigrationPreflightPolicy.MaximumCursorPayloadBytes)
                return false;

            var value = JsonSerializer.Deserialize<IdentityMigrationPreflightCursor>(json, JsonOptions);
            if (value is null || !IsValidAuthenticatedClaims(value, protectorExpiration))
                return false;

            cursor = value;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private bool IsValidAuthenticatedClaims(IdentityMigrationPreflightCursor cursor,
        DateTimeOffset protectorExpiration)
    {
        var now = _timeProvider.GetUtcNow();
        return cursor.PolicyVersion == IdentityMigrationPreflightPolicy.PolicyVersion
            && string.Equals(cursor.StoreSelection, IdentityMigrationPreflightPolicy.StoreSelection, StringComparison.Ordinal)
            && string.Equals(cursor.SchemaAssumptionVersion,
                IdentityMigrationPreflightPolicy.SchemaAssumptionVersion, StringComparison.Ordinal)
            && Enum.IsDefined(cursor.Phase)
            && cursor.Key is not null
            && cursor.Key.Length <= IdentityMigrationPreflightPolicy.MaximumKeyCharacters
            && !string.IsNullOrWhiteSpace(cursor.SchemaAssumptions)
            && cursor.SchemaAssumptions.Length <= IdentityMigrationPreflightPolicy.MaximumSchemaAssumptionsCharacters
            && cursor.ExpiresAtUtc > now
            && cursor.ExpiresAtUtc <= now.Add(IdentityMigrationPreflightPolicy.CursorLifetime)
            && cursor.ExpiresAtUtc <= protectorExpiration;
    }

    private static void ValidateForProtection(IdentityMigrationPreflightCursor cursor)
    {
        if (!Enum.IsDefined(cursor.Phase)) throw new ArgumentException("Cursor phase is invalid.", nameof(cursor));
        if (cursor.Key is null || cursor.Key.Length > IdentityMigrationPreflightPolicy.MaximumKeyCharacters)
            throw new ArgumentException("Cursor key is invalid.", nameof(cursor));
        if (string.IsNullOrWhiteSpace(cursor.SchemaAssumptions)
            || cursor.SchemaAssumptions.Length > IdentityMigrationPreflightPolicy.MaximumSchemaAssumptionsCharacters)
            throw new ArgumentException("Cursor schema assumptions are invalid.", nameof(cursor));
    }
}

internal sealed record IdentityMigrationPreflightCursor(
    int PolicyVersion,
    string StoreSelection,
    string SchemaAssumptionVersion,
    string SchemaAssumptions,
    IdentityMigrationPreflightPhase Phase,
    string Key,
    DateTimeOffset ExpiresAtUtc);
