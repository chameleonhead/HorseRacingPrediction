using System.Text.Json;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationPreviewTests
{
    private const string OldHorseId = "horse-11111111-1111-4111-8111-111111111111";
    private const string JraIdentity = "jra-horse:2024100828";

    [TestMethod]
    public async Task RealRegisteredEventFixtureIsStageableAndReadOnlyWhileLegacyInspectionStaysDisabled()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var before = await fixture.SourceSnapshotAsync();
        var engine = fixture.CreateEngineWithEventFlow();

        var preview = await engine.InspectPreviewAsync(Manifest(), CancellationToken.None);
        var legacy = await engine.InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsTrue(preview.CanStage, string.Join(',', preview.Blockers));
        Assert.IsNotNull(preview.Fingerprint);
        Assert.AreEqual(64, preview.Fingerprint!.Length);
        Assert.IsTrue(preview.Pages.Any(page => page.Table == "EventEntity"));
        Assert.AreEqual(before, await fixture.SourceSnapshotAsync());
        Assert.IsFalse(legacy.CanWrite, "Preview readiness must not change the legacy inspection contract.");

        var json = JsonSerializer.Serialize(preview, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(fixture.Root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eventJsonSerializer", json, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task InvalidManifestIsRejectedBeforeAnyStoreRead()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync(createEventStore: false);
        var invalid = new HorseIdentityMigrationManifest([
            new HorseIdentityMigrationManifestEntry("not-canonical", JraIdentity, true),
        ]);

        var preview = await fixture.CreateEngineWithEventFlow().InspectPreviewAsync(invalid, CancellationToken.None);

        Assert.IsFalse(preview.CanStage);
        CollectionAssert.AreEqual(new[] { HorseIdentityMigrationPreviewBlocker.InvalidManifest }, preview.Blockers.ToArray());
        Assert.IsNull(preview.Fingerprint);
        Assert.AreEqual(0, preview.Stores.Count);
    }

    [TestMethod]
    public async Task FingerprintIgnoresEvidenceOrderButTracksPersistedNonIdentityAndSchemaEvidence()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var engine = fixture.CreateEngineWithEventFlow();
        var validation = HorseIdentityMigrationManifestPolicy.Validate(Manifest());
        var snapshot = await engine.InspectExecutionInputsAsync(Manifest(), CancellationToken.None);
        var original = TypedHorseIdentityStoreEngine.BuildPreviewFingerprint(validation.Mappings, snapshot);
        var reordered = snapshot with
        {
            Schemas = snapshot.Schemas.Reverse().ToArray(),
            Roots = snapshot.Roots.Reverse().ToArray(),
            TypedReferences = snapshot.TypedReferences! with { Pages = snapshot.TypedReferences.Pages.Reverse().ToArray() },
        };
        Assert.AreEqual(original, TypedHorseIdentityStoreEngine.BuildPreviewFingerprint(validation.Mappings, reordered));

        var firstPage = snapshot.TypedReferences!.Pages[0];
        var persistedValueDrift = snapshot with
        {
            TypedReferences = snapshot.TypedReferences with
            {
                Pages = [firstPage with { BeforeHash = new string('A', 64) }, .. snapshot.TypedReferences.Pages.Skip(1)],
            },
        };
        Assert.AreNotEqual(original, TypedHorseIdentityStoreEngine.BuildPreviewFingerprint(validation.Mappings, persistedValueDrift));
        var schemaDrift = snapshot with
        {
            Schemas = [snapshot.Schemas[0] with { Fingerprint = new string('B', 64) }, .. snapshot.Schemas.Skip(1)],
        };
        Assert.AreNotEqual(original, TypedHorseIdentityStoreEngine.BuildPreviewFingerprint(validation.Mappings, schemaDrift));
        var rootDrift = snapshot with
        {
            Roots = [snapshot.Roots[0] with { TargetFailureNotificationExists = true }],
        };
        Assert.AreNotEqual(original, TypedHorseIdentityStoreEngine.BuildPreviewFingerprint(validation.Mappings, rootDrift));
        var pageMembershipDrift = snapshot with
        {
            TypedReferences = snapshot.TypedReferences with
            {
                Pages = [firstPage with { FirstKey = "different-key" }, .. snapshot.TypedReferences.Pages.Skip(1)],
            },
        };
        Assert.AreNotEqual(original, TypedHorseIdentityStoreEngine.BuildPreviewFingerprint(validation.Mappings, pageMembershipDrift));
        var mappingDrift = validation.Mappings.Select(mapping => mapping with { JraIdentity = "jra-horse:2024100829" }).ToArray();
        Assert.AreNotEqual(original, TypedHorseIdentityStoreEngine.BuildPreviewFingerprint(mappingDrift, snapshot));
    }

    [TestMethod]
    public async Task ExistingDestinationCollisionBlocksPreviewWithoutChangingSource()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedHorseAsync(DestinationHorseId());
        var before = await fixture.SourceSnapshotAsync();

        var preview = await fixture.CreateEngineWithEventFlow().InspectPreviewAsync(Manifest(), CancellationToken.None);

        Assert.IsFalse(preview.CanStage);
        CollectionAssert.Contains(preview.Blockers.ToArray(), HorseIdentityMigrationPreviewBlocker.DestinationCollision);
        Assert.AreEqual(before, await fixture.SourceSnapshotAsync());
    }

    [TestMethod]
    public async Task MissingRegisteredEventCodecAndOversizedSummaryCannotBecomeStageable()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var preview = await fixture.CreateEngine().InspectPreviewAsync(Manifest(), CancellationToken.None);

        Assert.IsFalse(preview.CanStage);
        CollectionAssert.Contains(preview.Blockers.ToArray(), HorseIdentityMigrationPreviewBlocker.IncompleteEvidence);

        var oversized = new HorseIdentityMigrationPreview(true, new string('C', 64), [], [], [], [],
            [new("EventStore", "EventEntity", 1, new string('D', HorseIdentityMigrationPreview.MaximumSummaryBytes), "hash")], 1, 1);
        var bounded = TypedHorseIdentityStoreEngine.BoundPreviewSummary(oversized);
        Assert.IsFalse(bounded.CanStage);
        CollectionAssert.AreEqual(new[] { HorseIdentityMigrationPreviewBlocker.SummaryTooLarge }, bounded.Blockers.ToArray());
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(bounded, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length
            <= HorseIdentityMigrationPreview.MaximumSummaryBytes);
    }

    private static HorseIdentityMigrationManifest Manifest() => new([
        new HorseIdentityMigrationManifestEntry(OldHorseId, JraIdentity, true),
    ]);

    private static string DestinationHorseId() => HorseIdentityMigrationManifestPolicy.Validate(Manifest())
        .Mappings.Single().DestinationHorseId;
}
