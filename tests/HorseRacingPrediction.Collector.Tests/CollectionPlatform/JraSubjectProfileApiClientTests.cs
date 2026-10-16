using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;
using System.Net;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraSubjectProfileApiClientTests
{
    [TestMethod]
    public async Task SaveAsync_PutsCanonicalProfileResourceWithProfileBody()
    {
        using var client = new HttpClient(new CollectionWorkerLeaseHandler
        {
            InnerHandler = new RecordingHandler(null, null),
        })
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        await new JraSubjectProfileApiClient(client).SaveAsync("Horse", "horse/1", Profile(), CancellationToken.None);
    }

    [TestMethod]
    public async Task SaveAsync_ForwardsCurrentLeaseHeaders()
    {
        var taskId = Guid.NewGuid();
        using var client = new HttpClient(new CollectionWorkerLeaseHandler
        {
            InnerHandler = new RecordingHandler(taskId.ToString("D"), "lease-token"),
        })
        { BaseAddress = new Uri("https://example.test/") };
        using var lease = CollectionWorkerLeaseContext.Push(taskId, "lease-token");

        await new JraSubjectProfileApiClient(client).SaveAsync("Horse", "horse/1", Profile(), CancellationToken.None);
    }

    [TestMethod]
    public async Task ConflictParser_OnlyClassifiesExactBoundedObjectCode()
    {
        await Assert.ThrowsExactlyAsync<StaleCollectionTaskLeaseException>(() => SendConflictAsync("{\"code\":\"StaleSubjectProfileLease\"}"));
        await Assert.ThrowsExactlyAsync<CollectionRepairHeldException>(() => SendConflictAsync("{\"code\":\"RaceRepairHeld\"}"));
        await Assert.ThrowsExactlyAsync<CollectionRepairHeldException>(() => SendConflictAsync("{\"code\":\"StaleRaceAssignmentFence\"}"));
        await Assert.ThrowsExactlyAsync<StaleCollectionTaskLeaseException>(() => SendConflictAsync(
            new UnknownLengthConflictContent("{\"code\":\"StaleSubjectProfileLease\"}")));
        await Assert.ThrowsExactlyAsync<CollectionRepairHeldException>(() => SendConflictAsync(
            new UnknownLengthConflictContent("{\"code\":\"RaceRepairHeld\"}")));
        var oversized = "{\"code\":\"StaleSubjectProfileLease\",\"padding\":\"" + new string('x', 5000) + "\"}";
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendConflictAsync(new UnknownLengthConflictContent(oversized)));
        foreach (var body in new[]
                 {
                     "{\"code\":\"OtherConflict\"}",
                     "[\"StaleSubjectProfileLease\"]",
                     "{\"code\":7}",
                     "not-json",
                 })
        {
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => SendConflictAsync(body));
        }
    }

    [TestMethod]
    public async Task ConflictParser_StopsAfterBoundPlusSentinelOnUnknownLengthBody()
    {
        var oversized = "{\"code\":\"StaleSubjectProfileLease\",\"padding\":\"" + new string('x', 5000) + "\"}";
        using var invoker = new HttpMessageInvoker(new CollectionWorkerLeaseHandler
        {
            InnerHandler = new ConflictHandler(new GuardedUnknownLengthContent(oversized, 4097)),
        });
        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Put,
            "https://example.test/api/v2/admin/subjects/Horse/horse-1/profile"), CancellationToken.None);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task ConflictParser_DisposesResponseAndRethrowsStreamInspectionFailure()
    {
        var content = new ThrowingReadContent();
        using var invoker = new HttpMessageInvoker(new CollectionWorkerLeaseHandler
        {
            InnerHandler = new ConflictHandler(content),
        });
        var exception = await Assert.ThrowsExactlyAsync<IOException>(() => invoker.SendAsync(new HttpRequestMessage(
            HttpMethod.Put, "https://example.test/api/v2/admin/subjects/Horse/horse-1/profile"), CancellationToken.None));

        Assert.AreEqual("inspection failed", exception.Message);
        Assert.IsTrue(content.IsDisposed);
    }

    private static JraSubjectProfileDto Profile() => new("Horse", "テスト馬", "source-1", "https://example.test/horse",
        new() { ["父"] = "父馬" }, new DateTimeOffset(2026, 9, 28, 1, 2, 3, TimeSpan.Zero));

    private static async Task SendConflictAsync(string body)
        => await SendConflictAsync(new StringContent(body));

    private static async Task SendConflictAsync(HttpContent content)
    {
        using var client = new HttpClient(new CollectionWorkerLeaseHandler
        {
            InnerHandler = new ConflictHandler(content),
        })
        { BaseAddress = new Uri("https://example.test/") };
        await new JraSubjectProfileApiClient(client).SaveAsync("Horse", "horse/1", Profile(), CancellationToken.None);
    }

    private sealed class RecordingHandler(string? expectedTaskId, string? expectedLeaseToken) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual("/api/v2/admin/subjects/Horse/horse%2F1/profile", request.RequestUri!.AbsolutePath);
            Assert.AreEqual(expectedTaskId, request.Headers.TryGetValues("X-Collection-Task-Id", out var taskIds)
                ? taskIds.Single() : null);
            Assert.AreEqual(expectedLeaseToken, request.Headers.TryGetValues("X-Collection-Lease-Token", out var tokens)
                ? tokens.Single() : null);
            Assert.AreEqual("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var profile = body.RootElement.GetProperty("profile");
            Assert.AreEqual("Horse", profile.GetProperty("subjectType").GetString());
            Assert.AreEqual("テスト馬", profile.GetProperty("name").GetString());
            Assert.AreEqual("父馬", profile.GetProperty("fields").GetProperty("父").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ConflictHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = content });
    }

    private sealed class UnknownLengthConflictContent(string body) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(body);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(_bytes));
    }

    private sealed class GuardedUnknownLengthContent(string body, int maxBytes) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(body);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new GuardedReadStream(_bytes, maxBytes));
    }

    private sealed class GuardedReadStream(byte[] bytes, int maxBytes) : MemoryStream(bytes, writable: false)
    {
        private int _totalRead;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_totalRead >= maxBytes)
                throw new InvalidOperationException("Conflict body read exceeded the bounded parser limit.");
            var read = base.Read(buffer.Span[..Math.Min(buffer.Length, maxBytes - _totalRead)]);
            _totalRead += read;
            return ValueTask.FromResult(read);
        }
    }

    private sealed class ThrowingReadContent : HttpContent
    {
        public bool IsDisposed { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new NotSupportedException();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new ThrowingReadStream());
        protected override void Dispose(bool disposing)
        {
            if (disposing) IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("inspection failed");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("inspection failed"));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
