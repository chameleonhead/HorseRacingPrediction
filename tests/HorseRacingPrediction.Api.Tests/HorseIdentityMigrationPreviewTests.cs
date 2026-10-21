using System.Text.Json;
using System.Text;
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
    public async Task ConcurrentFirstUseAndWarmPreviewsKeepEvidenceStableAndReadOnly()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var sourceBefore = await fixture.SourceSnapshotAsync();

        var concurrentPreviews = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => fixture.CreateEngineWithEventFlow().InspectPreviewAsync(Manifest(), CancellationToken.None))));
        var warmPreview = await fixture.CreateEngineWithEventFlow()
            .InspectPreviewAsync(Manifest(), CancellationToken.None);

        static string SerializeStablePreview(HorseIdentityMigrationPreview preview)
        {
            var normalized = preview with
            {
                SchemaDiagnostics = preview.SchemaDiagnostics is { } schema
                    ? schema with { ObservedAtUtc = DateTimeOffset.UnixEpoch }
                    : null,
                InspectionDiagnostic = preview.InspectionDiagnostic is { } diagnostic
                    ? diagnostic with { ElapsedMilliseconds = 0 }
                    : null,
            };
            return JsonSerializer.Serialize(normalized, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        Assert.IsTrue(warmPreview.CanStage, string.Join(',', warmPreview.Blockers));
        Assert.IsNotNull(warmPreview.Fingerprint);
        Assert.IsTrue(concurrentPreviews.All(preview => preview.CanStage));
        foreach (var concurrentPreview in concurrentPreviews)
        {
            Assert.AreEqual(warmPreview.Fingerprint, concurrentPreview.Fingerprint);
            Assert.AreEqual(warmPreview.InspectedRows, concurrentPreview.InspectedRows);
            Assert.AreEqual(warmPreview.InspectedBytes, concurrentPreview.InspectedBytes);
            CollectionAssert.AreEqual(warmPreview.Pages.ToArray(), concurrentPreview.Pages.ToArray());
            Assert.AreEqual(SerializeStablePreview(warmPreview), SerializeStablePreview(concurrentPreview));
        }

        Assert.AreEqual(sourceBefore, await fixture.SourceSnapshotAsync());
    }

    [TestMethod]
    public async Task OptionalInspectionProgressDoesNotChangePreviewFingerprintAndLegacyPayloadsRemainValid()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var engine = fixture.CreateEngineWithEventFlow();

        var legacyCall = await engine.InspectPreviewAsync(Manifest(), CancellationToken.None);
        var progress = new HorseIdentityMigrationInspectionProgress();
        var observedCall = await engine.InspectPreviewAsync(Manifest(), CancellationToken.None, progress);

        Assert.AreEqual(legacyCall.Fingerprint, observedCall.Fingerprint);
        Assert.IsTrue(observedCall.CanStage);
        Assert.IsNotNull(observedCall.InspectionDiagnostic);
        Assert.AreEqual(HorseIdentityMigrationInspectionResult.Completed, observedCall.InspectionDiagnostic.Result);
        Assert.IsNull(legacyCall.InspectionDiagnostic);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacyJson = JsonSerializer.Serialize(legacyCall, options);
        Assert.DoesNotContain("inspectionDiagnostic", legacyJson, StringComparison.Ordinal);
        var parsedLegacy = JsonSerializer.Deserialize<HorseIdentityMigrationPreview>("""
            {"canStage":false,"fingerprint":null,"blockers":[3],"mappings":[],"stores":[],"roots":[],"pages":[],"inspectedRows":0,"inspectedBytes":0}
            """, options);
        Assert.IsNotNull(parsedLegacy);
        Assert.IsNull(parsedLegacy.InspectionDiagnostic);
    }

    [TestMethod]
    public async Task LargeUnrelatedEventStoreStaysStageableAndFingerprintBindsOmittedPages()
    {
        await using var fixture = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await SeedUnrelatedHorseEventsAsync(fixture, 10_000);
        var engine = fixture.CreateEngineWithEventFlow();
        var snapshot = await fixture.CreateReadersWithEventFlow().InspectAsync(
            HorseIdentityMigrationManifestPolicy.Validate(Manifest()).Mappings, CancellationToken.None);

        var preview = await engine.InspectPreviewAsync(Manifest(), CancellationToken.None);

        Assert.IsTrue(preview.CanStage, string.Join(',', preview.Blockers));
        Assert.IsTrue(snapshot.TypedReferenceClosureInspected);
        Assert.IsTrue(snapshot.TypedReferences?.IsComplete, snapshot.TypedReferences?.Failure);
        var unfilteredPages = snapshot.TypedReferences!.Pages.Select(page => new HorseIdentityMigrationPreviewPage(
            page.Surface.ToString(), page.Table, page.RowCount, page.BeforeHash, page.ExpectedAfterHash)).ToArray();
        var unfilteredPreview = preview with { Pages = unfilteredPages };
        var unfilteredSummaryBytes = JsonSerializer.SerializeToUtf8Bytes(unfilteredPreview,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
        var internalSummaryBytes = snapshot.TypedReferences.Pages.Sum(page =>
            Encoding.UTF8.GetByteCount(page.Surface.ToString()) + Encoding.UTF8.GetByteCount(page.Table)
            + Encoding.UTF8.GetByteCount(page.BeforeHash) + Encoding.UTF8.GetByteCount(page.ExpectedAfterHash)
            + (page.FirstKey is null ? 0 : Encoding.UTF8.GetByteCount(page.FirstKey))
            + (page.LastKey is null ? 0 : Encoding.UTF8.GetByteCount(page.LastKey)) + sizeof(long) + sizeof(int));
        Assert.IsGreaterThan(HorseIdentityMigrationPreview.MaximumSummaryBytes, unfilteredSummaryBytes,
            "The prior public all-pages summary must exceed its response cap.");
        Assert.IsLessThan(HorseIdentityMigrationPreview.MaximumSummaryBytes, internalSummaryBytes,
            "The fixture must stay within the reader's independent immutable-evidence budget.");
        Assert.AreEqual(HorseIdentityMigrationPreviewBlocker.SummaryTooLarge,
            TypedHorseIdentityStoreEngine.BoundPreviewSummary(unfilteredPreview).Blockers.Single());
        Assert.IsNotNull(preview.Fingerprint);
        Assert.IsGreaterThan(10_000L, preview.InspectedRows);
        Assert.IsGreaterThan(1_000_000L, preview.InspectedBytes);
        Assert.IsLessThan(10, preview.Pages.Count, "Unchanged evidence pages should not appear in the operator's conversion-page list.");
        Assert.IsTrue(preview.Pages.All(page => !string.Equals(page.BeforeHash, page.ExpectedAfterHash,
            StringComparison.Ordinal)));
        Assert.IsTrue(preview.Pages.Any(page => page.Table == "EventEntity"));

        var visiblePages = preview.Pages.ToArray();
        var visibleFingerprint = preview.Fingerprint;
        await fixture.ExecuteAsync(fixture.EventStorePath,
            "UPDATE EventEntity SET BatchId = 'mutated-batch' WHERE GlobalSequenceNumber = 10001;");

        var changedUnrelatedEvidence = await engine.InspectPreviewAsync(Manifest(), CancellationToken.None);

        Assert.IsTrue(changedUnrelatedEvidence.CanStage, string.Join(',', changedUnrelatedEvidence.Blockers));
        Assert.AreNotEqual(visibleFingerprint, changedUnrelatedEvidence.Fingerprint,
            "The fingerprint must still bind changes to internal pages omitted from the public conversion list.");
        CollectionAssert.AreEqual(visiblePages, changedUnrelatedEvidence.Pages.ToArray(),
            "A non-transformed page remains absent even though its data still changes the full fingerprint.");
        Assert.AreEqual(preview.InspectedRows, changedUnrelatedEvidence.InspectedRows);
        Assert.AreEqual(preview.InspectedBytes, changedUnrelatedEvidence.InspectedBytes);
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

    private static Task SeedUnrelatedHorseEventsAsync(
        HorseIdentityMigrationStoreEngineTests.StoreFixture fixture, int count)
    {
        return fixture.ExecuteAsync(fixture.EventStorePath, $$"""
            WITH RECURSIVE seq(value) AS (
                SELECT 2 UNION ALL SELECT value + 1 FROM seq WHERE value < {{count + 1}}
            )
            , unrelated(value, stream_key, stream_sequence) AS (
                SELECT value, (value - 2) / 2, ((value - 2) % 2) + 1 FROM seq
            )
            INSERT INTO EventEntity
                (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
            SELECT unrelated.value,
                   'horse-33333333-3333-4333-8333-' || printf('%012d', unrelated.stream_key + 1),
                   source.AggregateName, unrelated.stream_sequence, source.BatchId, source.Data,
                   replace(replace(replace(source.Metadata, '{{OldHorseId}}',
                       'horse-33333333-3333-4333-8333-' || printf('%012d', unrelated.stream_key + 1)),
                       '"aggregate_sequence_number":"1"',
                       '"aggregate_sequence_number":"' || unrelated.stream_sequence || '"'),
                       '"global_sequence_number":"1"',
                       '"global_sequence_number":"' || unrelated.value || '"')
            FROM unrelated CROSS JOIN EventEntity AS source
            WHERE source.AggregateId = '{{OldHorseId}}';
            """);
    }

    private static string DestinationHorseId() => HorseIdentityMigrationManifestPolicy.Validate(Manifest())
        .Mappings.Single().DestinationHorseId;
}
