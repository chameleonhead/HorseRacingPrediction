using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HorseRacingPrediction.Api.IdentityMigration;

/// <summary>
/// Private authorization envelopes passed only to an explicitly selected child process through its
/// private environment. Bootstrap authority and operation authority are intentionally different types.
/// </summary>
internal static class HorseIdentityMigrationMaintenanceCapability
{
    internal const string PrivateEnvironmentVariable = "HRP_INTERNAL_HORSE_MIGRATION_AUTH";
    private const string Version = "v1";

    internal sealed class ApiBootstrapAuthorization
    {
        internal ApiBootstrapAuthorization(string sourceSetHash, int parentProcessId, long parentStartTicks, byte[] nonce)
        {
            SourceSetHash = sourceSetHash;
            ParentProcessId = parentProcessId;
            ParentStartTicks = parentStartTicks;
            Nonce = nonce;
        }

        internal string SourceSetHash { get; }
        internal int ParentProcessId { get; }
        internal long ParentStartTicks { get; }
        private byte[] Nonce { get; }

        internal string SerializeForPrivateEnvironment() => Encode([
            Version, "api-bootstrap", SourceSetHash, ParentProcessId.ToString(CultureInfo.InvariantCulture),
            ParentStartTicks.ToString(CultureInfo.InvariantCulture), Convert.ToHexString(Nonce)]);

        internal static bool TryImportAndValidate(string? value, IEnumerable<string> sourcePaths,
            out ApiBootstrapAuthorization? authorization)
        {
            authorization = null;
            if (!TryDecode(value, 6, out var fields) || fields[0] != Version || fields[1] != "api-bootstrap"
                || !IsHash(fields[2]) || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                || pid <= 0 || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var startTicks)
                || startTicks <= 0 || !TryNonce(fields[5], out var nonce)) return false;

            var candidate = new ApiBootstrapAuthorization(fields[2], pid, startTicks, nonce);
            if (!HorseIdentityMigrationMaintenanceOwnership.ValidateChildAuthorizationForSources(sourcePaths,
                    candidate.SourceSetHash, candidate.ParentProcessId, candidate.ParentStartTicks,
                    HashNonce(candidate.Nonce), "api-bootstrap", operationId: null, requestHash: null)) return false;
            if (!string.Equals(candidate.SourceSetHash,
                    HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths), StringComparison.Ordinal))
                return false;

            authorization = candidate;
            return true;
        }

        internal string GetNonceHash() => HashNonce(Nonce);

        internal bool ValidateForSourcePaths(IEnumerable<string> sourcePaths) =>
            HorseIdentityMigrationMaintenanceOwnership.ValidateParentAuthorizationForSources(sourcePaths,
                SourceSetHash, ParentProcessId, ParentStartTicks, GetNonceHash(), "api-bootstrap",
                operationId: null, requestHash: null)
            && string.Equals(SourceSetHash,
                HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths), StringComparison.Ordinal);
    }

    internal sealed class OperationAuthorization
    {
        internal OperationAuthorization(Guid operationId, string requestHash, string sourceSetHash,
            int parentProcessId, long parentStartTicks, byte[] nonce)
        {
            OperationId = operationId;
            RequestHash = requestHash;
            SourceSetHash = sourceSetHash;
            ParentProcessId = parentProcessId;
            ParentStartTicks = parentStartTicks;
            Nonce = nonce;
        }

        internal Guid OperationId { get; }
        internal string RequestHash { get; }
        internal string SourceSetHash { get; }
        internal int ParentProcessId { get; }
        internal long ParentStartTicks { get; }
        private byte[] Nonce { get; }

        internal string SerializeForPrivateEnvironment() => Encode([
            Version, "cli-operation", OperationId.ToString("D"), RequestHash, SourceSetHash,
            ParentProcessId.ToString(CultureInfo.InvariantCulture), ParentStartTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(Nonce)]);

        internal static bool TryImportAndValidate(string? value, IEnumerable<string> sourcePaths,
            out OperationAuthorization? authorization)
        {
            authorization = null;
            if (!TryDecode(value, 8, out var fields) || fields[0] != Version || fields[1] != "cli-operation"
                || !Guid.TryParseExact(fields[2], "D", out var operationId) || operationId == Guid.Empty
                || !IsHash(fields[3]) || !IsHash(fields[4])
                || !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0
                || !long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var startTicks)
                || startTicks <= 0 || !TryNonce(fields[7], out var nonce)) return false;

            var candidate = new OperationAuthorization(operationId, fields[3], fields[4], pid, startTicks, nonce);
            if (!string.Equals(candidate.SourceSetHash,
                    HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths), StringComparison.Ordinal)
                || !HorseIdentityMigrationMaintenanceOwnership.ValidateChildAuthorizationForSources(sourcePaths,
                    candidate.SourceSetHash, candidate.ParentProcessId, candidate.ParentStartTicks,
                    HashNonce(candidate.Nonce), "cli-operation", candidate.OperationId, candidate.RequestHash)
                || !HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(
                    sourcePaths, candidate.OperationId, candidate.RequestHash, candidate.SourceSetHash)) return false;

            authorization = candidate;
            return true;
        }

        internal string GetNonceHash() => HashNonce(Nonce);

        internal bool ValidateForSourcePaths(IEnumerable<string> sourcePaths) =>
            string.Equals(SourceSetHash,
                HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths), StringComparison.Ordinal)
            && HorseIdentityMigrationMaintenanceOwnership.ValidateParentAuthorizationForSources(sourcePaths,
                SourceSetHash, ParentProcessId, ParentStartTicks, GetNonceHash(), "cli-operation",
                OperationId, RequestHash)
            && HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(
                sourcePaths, OperationId, RequestHash, SourceSetHash);
    }

    internal sealed class ReadonlyReverificationAuthorization
    {
        private readonly byte[] _nonce;

        internal ReadonlyReverificationAuthorization(Guid operationId, string requestHash, string sourceSetHash,
            int parentProcessId, long parentStartTicks, byte[] nonce)
        {
            OperationId = operationId;
            RequestHash = requestHash;
            SourceSetHash = sourceSetHash;
            ParentProcessId = parentProcessId;
            ParentStartTicks = parentStartTicks;
            _nonce = nonce.ToArray();
        }

        internal Guid OperationId { get; }
        internal string RequestHash { get; }
        internal string SourceSetHash { get; }
        internal int ParentProcessId { get; }
        internal long ParentStartTicks { get; }
        internal string GetNonceHash() => HashNonce(_nonce);

        internal bool ValidateForSourcePaths(IEnumerable<string> sourcePaths) =>
            string.Equals(SourceSetHash,
                HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths), StringComparison.Ordinal)
            && HorseIdentityMigrationMaintenanceOwnership.ValidateParentAuthorizationForSources(sourcePaths,
                SourceSetHash, ParentProcessId, ParentStartTicks, GetNonceHash(), "readonly-reverify",
                OperationId, RequestHash)
            && HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(
                sourcePaths, OperationId, RequestHash, SourceSetHash);

        internal async Task<bool> ValidateForSourcePathsAsync(IEnumerable<string> sourcePaths,
            HorseIdentityMigrationJournal journal, CancellationToken cancellationToken = default)
        {
            if (!ValidateForSourcePaths(sourcePaths)) return false;
            var status = await journal.GetStatusAsync(OperationId, cancellationToken).ConfigureAwait(false);
            var intent = await journal.GetExecutionIntentAsync(OperationId, cancellationToken).ConfigureAwait(false);
            return status?.Status == HorseIdentityMigrationOperationStatus.Succeeded
                && intent?.ImmutableRequestHash == RequestHash
                && intent.Phase is HorseIdentityMigrationExecutionPhase.DataSucceeded
                    or HorseIdentityMigrationExecutionPhase.Verifying;
        }

        internal string SerializeForPrivateEnvironment() => Encode([
            Version, "readonly-reverify", OperationId.ToString("D"), RequestHash, SourceSetHash,
            ParentProcessId.ToString(CultureInfo.InvariantCulture), ParentStartTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(_nonce)]);

        internal static async Task<ReadonlyReverificationAuthorization?> ImportAndValidateAsync(
            string? value, IEnumerable<string> sourcePaths, HorseIdentityMigrationJournal journal,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(journal);
            if (!TryDecode(value, 8, out var fields) || fields[0] != Version || fields[1] != "readonly-reverify"
                || !Guid.TryParseExact(fields[2], "D", out var operationId) || operationId == Guid.Empty
                || !IsHash(fields[3]) || !IsHash(fields[4])
                || !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0
                || !long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var startTicks)
                || startTicks <= 0 || !TryNonce(fields[7], out var nonce)) return null;

            var candidate = new ReadonlyReverificationAuthorization(operationId, fields[3], fields[4], pid,
                startTicks, nonce);
            if (!string.Equals(candidate.SourceSetHash,
                    HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths), StringComparison.Ordinal)
                || !HorseIdentityMigrationMaintenanceOwnership.ValidateChildAuthorizationForSources(sourcePaths,
                    candidate.SourceSetHash, candidate.ParentProcessId, candidate.ParentStartTicks,
                    candidate.GetNonceHash(), "readonly-reverify", candidate.OperationId, candidate.RequestHash)
                || !HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(
                    sourcePaths, candidate.OperationId, candidate.RequestHash, candidate.SourceSetHash)) return null;

            var status = await journal.GetStatusAsync(candidate.OperationId, cancellationToken).ConfigureAwait(false);
            var intent = await journal.GetExecutionIntentAsync(candidate.OperationId, cancellationToken).ConfigureAwait(false);
            return status?.Status == HorseIdentityMigrationOperationStatus.Succeeded
                && intent?.ImmutableRequestHash == candidate.RequestHash
                && intent.Phase is HorseIdentityMigrationExecutionPhase.DataSucceeded
                    or HorseIdentityMigrationExecutionPhase.Verifying
                ? candidate : null;
        }
    }

    /// <summary>In-process proof that the live supervisor control lease validated preparation cancellation.</summary>
    internal sealed class PreparationCancellationAuthorization
    {
        private readonly byte[] _nonce;

        internal PreparationCancellationAuthorization(Guid operationId, string requestHash, string sourceSetHash,
            int parentProcessId, long parentStartTicks, byte[] nonce)
        {
            OperationId = operationId;
            RequestHash = requestHash;
            SourceSetHash = sourceSetHash;
            ParentProcessId = parentProcessId;
            ParentStartTicks = parentStartTicks;
            _nonce = nonce.ToArray();
        }

        internal Guid OperationId { get; }
        internal string RequestHash { get; }
        internal string SourceSetHash { get; }
        internal int ParentProcessId { get; }
        internal long ParentStartTicks { get; }
        internal string GetNonceHash() => HashNonce(_nonce);

        internal bool ValidateForSourcePaths(IEnumerable<string> sourcePaths) =>
            string.Equals(SourceSetHash,
                HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sourcePaths), StringComparison.Ordinal)
            && HorseIdentityMigrationMaintenanceOwnership.ValidateParentAuthorizationForSources(sourcePaths,
                SourceSetHash, ParentProcessId, ParentStartTicks, GetNonceHash(), "preparation-cancel",
                OperationId, RequestHash)
            && HorseIdentityMigrationMaintenanceOwnership.ValidateMaintenanceGateSet(
                sourcePaths, OperationId, RequestHash, SourceSetHash);
    }

    internal static ApiBootstrapAuthorization CreateApiBootstrapAuthorization(
        string sourceSetHash, int parentProcessId, long parentStartTicks, byte[] nonce)
    {
        if (!IsHash(sourceSetHash) || parentProcessId <= 0 || parentStartTicks <= 0 || nonce.Length != 32)
            throw new ArgumentException("The API bootstrap authorization fields are invalid.");
        return new(sourceSetHash, parentProcessId, parentStartTicks, nonce.ToArray());
    }

    internal static OperationAuthorization CreateOperationAuthorization(Guid operationId, string requestHash,
        string sourceSetHash, int parentProcessId, long parentStartTicks, byte[] nonce)
    {
        if (operationId == Guid.Empty || !IsHash(requestHash) || !IsHash(sourceSetHash)
            || parentProcessId <= 0 || parentStartTicks <= 0 || nonce.Length != 32)
            throw new ArgumentException("The CLI operation authorization fields are invalid.");
        return new(operationId, requestHash, sourceSetHash, parentProcessId, parentStartTicks, nonce.ToArray());
    }

    internal static PreparationCancellationAuthorization CreatePreparationCancellationAuthorization(
        Guid operationId, string requestHash, string sourceSetHash, int parentProcessId, long parentStartTicks,
        byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        if (operationId == Guid.Empty || !IsHash(requestHash) || !IsHash(sourceSetHash)
            || parentProcessId <= 0 || parentStartTicks <= 0 || nonce.Length != 32)
            throw new ArgumentException("The preparation cancellation authorization fields are invalid.");
        return new(operationId, requestHash, sourceSetHash, parentProcessId, parentStartTicks, nonce.ToArray());
    }

    internal static byte[] CreateNonce() => RandomNumberGenerator.GetBytes(32);

    internal static string HashNonce(byte[] nonce) => Convert.ToHexString(SHA256.HashData(nonce));

    internal static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit)
        && string.Equals(value, value.ToUpperInvariant(), StringComparison.Ordinal);

    internal static (int ProcessId, long StartTicks) CurrentProcessIdentity()
    {
        using var process = Process.GetCurrentProcess();
        return (process.Id, HorseIdentityMigrationMaintenanceOwnership.GetProcessStartTicks(process.Id));
    }

    private static string Encode(string[] fields) => Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Join('\n', fields)));

    private static bool TryDecode(string? value, int fieldCount, out string[] fields)
    {
        fields = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048) return false;
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length > 1024) return false;
            var decoded = Encoding.ASCII.GetString(bytes);
            if (decoded.Any(character => (character is < ' ' and not '\n') || character > '~')) return false;
            fields = decoded.Split('\n');
            return fields.Length == fieldCount;
        }
        catch (FormatException) { return false; }
    }

    private static bool TryNonce(string value, out byte[] nonce)
    {
        nonce = [];
        if (value.Length != 64) return false;
        try
        {
            nonce = Convert.FromHexString(value);
            return nonce.Length == 32;
        }
        catch (FormatException) { return false; }
    }
}
