using System.Net;
using System.Net.Http.Json;
using System.Text;
using HorseRacingPrediction.Api.Endpoints.IdentityMigration;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationEndpointsTests
{
    private const string OldHorseId = "horse-11111111-1111-4111-8111-111111111111";
    private const string JraIdentity = "jra-horse:2024100828";

    [TestMethod]
    public async Task RealPreviewAndStageAreReadOnlyFreshnessBoundAndJournalOnly()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await stores.SeedHorseAsync(OldHorseId);
        await stores.SeedSerializedHorseEventAsync(OldHorseId);
        var before = await stores.SourceSnapshotAsync();
        await using var app = await StartApiAsync(stores);
        using var transport = app.GetTestClient();
        var client = new AdminApiClient(transport,
            new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { HeaderName = "X-Api-Key", Key = TestApplicationFactory.TestApiKey }));
        var manifest = Manifest();

        var preview = await client.PreviewHorseIdentityMigrationAsync(manifest);
        Assert.IsNotNull(preview);
        Assert.IsTrue(preview.CanStage, string.Join(',', preview.Blockers));
        Assert.IsNotNull(preview.Fingerprint);
        Assert.AreEqual(before, await stores.SourceSnapshotAsync());

        var staged = await client.StageHorseIdentityMigrationAsync(manifest, preview.Fingerprint!);
        Assert.IsNotNull(staged);
        Assert.AreEqual("AwaitingActions", staged.Status);
        Assert.AreEqual("None", staged.Reason);
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged,
            (await GetJournal(stores).GetStatusAsync(staged.OperationId))!.Status);

        var status = await client.GetHorseIdentityMigrationOperationAsync(staged.OperationId);
        Assert.IsNotNull(status);
        Assert.AreEqual("AwaitingActions", status.Status);
        using var rawStatus = await transport.GetAsync($"/api/v2/admin/horse-id-migration-operations/{staged.OperationId:D}");
        Assert.AreEqual("no-store, no-cache, max-age=0", rawStatus.Headers.CacheControl?.ToString());
        using var unknownStatus = await transport.GetAsync($"/api/v2/admin/horse-id-migration-operations/{Guid.NewGuid():D}");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownStatus.StatusCode);
        using var malformedStatus = await transport.GetAsync("/api/v2/admin/horse-id-migration-operations/not-a-guid");
        Assert.AreEqual(HttpStatusCode.NotFound, malformedStatus.StatusCode);
        Assert.AreEqual(before, await stores.SourceSnapshotAsync(), "Preview and stage must not write source stores.");
    }

    [TestMethod]
    public async Task ApiKeyIsCheckedBeforeBodyAndStaleOrInvalidInputCannotStage()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await stores.SeedHorseAsync(OldHorseId);
        await stores.SeedSerializedHorseEventAsync(OldHorseId);
        var before = await stores.SourceSnapshotAsync();
        await using var app = await StartApiAsync(stores);
        using var client = app.GetTestClient();

        using var unauthenticated = await client.PostAsync("/api/v2/admin/horse-id-migration-previews",
            new StringContent("{ definitely not json", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        using var invalid = await client.PostAsync("/api/v2/admin/horse-id-migration-previews",
            new StringContent("{ definitely not json", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        var manifest = Manifest();
        var validPreview = await client.PostAsJsonAsync("/api/v2/admin/horse-id-migration-previews", manifest);
        Assert.AreEqual(HttpStatusCode.OK, validPreview.StatusCode);
        var preview = await validPreview.Content.ReadFromJsonAsync<HorseIdentityMigrationPreview>();
        Assert.IsNotNull(preview?.Fingerprint);
        var stale = await client.PostAsJsonAsync("/api/v2/admin/horse-id-migration-operations",
            new HorseIdentityMigrationStageRequest(manifest, new string('0', 64)));
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        StringAssert.Contains(await stale.Content.ReadAsStringAsync(), "PreviewStale");
        Assert.IsFalse(File.Exists(Path.Combine(stores.Root, "horse-identity-migration-journal.db")),
            "A stale stage handle must not create or claim a journal operation.");
        Assert.AreEqual(before, await stores.SourceSnapshotAsync());
    }

    [TestMethod]
    public async Task SemanticInvalidManifestReturns400BeforeReadOrJournalAdmission()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        var before = await stores.SourceSnapshotAsync();
        await using var app = await StartApiAsync(stores);
        using var transport = app.GetTestClient();
        var client = new AdminApiClient(transport,
            new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { HeaderName = "X-Api-Key", Key = TestApplicationFactory.TestApiKey }));
        var invalid = new HorseIdentityMigrationManifest(
        [new HorseIdentityMigrationManifestEntry("not-canonical", JraIdentity, true)]);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PreviewHorseIdentityMigrationAsync(invalid));
        Assert.AreEqual(HttpStatusCode.BadRequest, failure.StatusCode);
        Assert.IsFalse(File.Exists(Path.Combine(stores.Root, "horse-identity-migration-journal.db")));
        Assert.AreEqual(before, await stores.SourceSnapshotAsync());
    }

    [TestMethod]
    public async Task OversizedRequestIsRejectedWithBoundedSafeError()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await stores.SeedHorseAsync(OldHorseId);
        await stores.SeedSerializedHorseEventAsync(OldHorseId);
        await using var app = await StartApiAsync(stores);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        using var response = await client.PostAsync("/api/v2/admin/horse-id-migration-previews",
            new StringContent(new string(' ', HorseIdentityMigrationJournal.MaximumRequestBytes + 1), Encoding.UTF8,
                "application/json"));
        Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "CapacityExceeded");
        Assert.IsFalse(body.Contains(stores.Root, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ChunkedUnknownLengthOverflowIsRejectedBeforeReadingSourceStores()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await stores.SeedHorseAsync(OldHorseId);
        await stores.SeedSerializedHorseEventAsync(OldHorseId);
        var before = await stores.SourceSnapshotAsync();
        await using var app = await StartApiAsync(stores);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/admin/horse-id-migration-previews")
        {
            Content = new StreamContent(new OversizedUnknownLengthStream(
                HorseIdentityMigrationJournal.MaximumRequestBytes + 32L)),
        };
        request.Headers.TransferEncodingChunked = true;
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.AreEqual(before, await stores.SourceSnapshotAsync());
    }

    [TestMethod]
    public async Task CorruptStatusJournalReturnsSafeServiceUnavailable()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(stores.Root, "horse-identity-migration-journal.db"), "not a sqlite database");
        await using var app = await StartApiAsync(stores);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        using var response = await client.GetAsync($"/api/v2/admin/horse-id-migration-operations/{Guid.NewGuid():D}");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Unavailable");
        Assert.IsFalse(body.Contains(stores.Root, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(body.Contains("not a sqlite database", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<WebApplication> StartApiAsync(
        HorseIdentityMigrationStoreEngineTests.StoreFixture stores)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        builder.Services.AddSingleton(stores.CreateEngineWithEventFlow());
        builder.Services.AddSingleton(GetJournal(stores));
        builder.Services.AddSingleton(TimeProvider.System);
        var app = builder.Build();
        app.UseApiKeyProtection();
        HorseIdentityMigrationEndpoints.Map(app.MapGroup("/api").AddEndpointFilter<ApiKeyEndpointFilter>());
        await app.StartAsync();
        return app;
    }

    private static HorseIdentityMigrationJournal GetJournal(
        HorseIdentityMigrationStoreEngineTests.StoreFixture stores)
        => new(stores.Root, [stores.EventStorePath, stores.CollectionPath, stores.SchedulePath]);

    private static HorseIdentityMigrationManifest Manifest() => new(
    [
        new HorseIdentityMigrationManifestEntry(OldHorseId, JraIdentity, true),
    ]);

    private sealed class OversizedUnknownLengthStream(long remaining) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..count].Fill((byte)' ');
            remaining -= count;
            return ValueTask.FromResult(count);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
