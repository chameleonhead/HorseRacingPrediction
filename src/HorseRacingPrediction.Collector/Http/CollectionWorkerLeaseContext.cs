namespace HorseRacingPrediction.Collector.Http;

public sealed record CollectionWorkerLease(Guid TaskId, string LeaseToken, long RaceHoldGeneration, string? AssignmentFingerprint);

public static class CollectionWorkerLeaseContext
{
    private static readonly AsyncLocal<CollectionWorkerLease?> CurrentLease = new();
    public static CollectionWorkerLease? Current => CurrentLease.Value;

    public static IDisposable Push(Guid taskId, string leaseToken, long raceHoldGeneration = 0, string? assignmentFingerprint = null)
    {
        var previous = CurrentLease.Value;
        CurrentLease.Value = new(taskId, leaseToken, raceHoldGeneration, assignmentFingerprint);
        return new Scope(() => CurrentLease.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class CollectionWorkerLeaseHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (CollectionWorkerLeaseContext.Current is { } lease)
        {
            request.Headers.TryAddWithoutValidation("X-Collection-Task-Id", lease.TaskId.ToString("D"));
            request.Headers.TryAddWithoutValidation("X-Collection-Lease-Token", lease.LeaseToken);
            request.Headers.TryAddWithoutValidation("X-Race-Hold-Generation", lease.RaceHoldGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (lease.AssignmentFingerprint is not null)
                request.Headers.TryAddWithoutValidation("X-Race-Assignment-Fingerprint", lease.AssignmentFingerprint);
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            try
            {
                var (boundedBody, code) = await ReadBoundedConflictCodeAsync(response, cancellationToken);
                if (code == "StaleSubjectProfileLease" && boundedBody)
                {
                    throw new HorseRacingPrediction.Contracts.Collection.StaleCollectionTaskLeaseException();
                }
                if (code is "RaceRepairHeld" or "StaleRaceAssignmentFence")
                {
                    throw new HorseRacingPrediction.Contracts.Collection.CollectionRepairHeldException();
                }
            }
            catch (System.Text.Json.JsonException) { }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        return response;
    }

    private static async Task<(bool Bounded, string? Code)> ReadBoundedConflictCodeAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maxBytes = 4096;
        var originalContent = response.Content;
        var originalStream = await originalContent.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[maxBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await originalStream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }

        var bounded = length <= maxBytes;
        var prefix = buffer.AsMemory(0, length).ToArray();
        HttpContent replacement = bounded
            ? new ByteArrayContent(prefix)
            : new StreamContent(new PrefixThenTailStream(prefix, originalStream, originalContent));
        foreach (var header in originalContent.Headers)
            if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content = replacement;
        if (bounded) originalContent.Dispose();
        if (!bounded) return (false, null);

        using var payload = System.Text.Json.JsonDocument.Parse(prefix);
        if (payload.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            && payload.RootElement.TryGetProperty("code", out var code)
            && code.ValueKind == System.Text.Json.JsonValueKind.String)
            return (true, code.GetString());
        return (true, null);
    }

    private sealed class PrefixThenTailStream(byte[] prefix, Stream tail, HttpContent owner) : Stream
    {
        private readonly MemoryStream _prefix = new(prefix, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _prefix.Read(buffer, offset, count);
            return read != 0 ? read : tail.Read(buffer, offset, count);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _prefix.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return read != 0 ? read : await tail.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = _prefix.Read(buffer, offset, count);
            return read != 0 ? Task.FromResult(read) : tail.ReadAsync(buffer, offset, count, cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _prefix.Dispose();
                tail.Dispose();
                owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
