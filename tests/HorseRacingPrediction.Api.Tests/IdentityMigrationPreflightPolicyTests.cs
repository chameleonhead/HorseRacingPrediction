using System.Text.Json;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class IdentityMigrationPreflightPolicyTests
{
    [TestMethod]
    public void FixedPhasesAndBounds_AreStableAndBounded()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                IdentityMigrationPreflightPhase.HorseRoots,
                IdentityMigrationPreflightPhase.HorseEvents,
                IdentityMigrationPreflightPhase.JraProfiles,
                IdentityMigrationPreflightPhase.HorseRaceHistories,
                IdentityMigrationPreflightPhase.HorseWeightHistories,
                IdentityMigrationPreflightPhase.CollectionResources,
            },
            IdentityMigrationPreflightPolicy.GetPhases().ToArray());
        AssertExpected(128, IdentityMigrationPreflightPolicy.PageSize);
        Assert.AreEqual(TimeSpan.FromSeconds(30), IdentityMigrationPreflightPolicy.RequestDeadline);
        Assert.AreEqual(TimeSpan.FromMinutes(30), IdentityMigrationPreflightPolicy.CursorLifetime);
        AssertExpected(4 * 1024, IdentityMigrationPreflightPolicy.MaximumCursorCharacters);
        Assert.AreEqual(IdentityMigrationPreflightPhase.HorseEvents,
            IdentityMigrationPreflightPolicy.GetNextPhase(IdentityMigrationPreflightPhase.HorseRoots));
        Assert.IsNull(IdentityMigrationPreflightPolicy.GetNextPhase(
            IdentityMigrationPreflightPhase.CollectionResources));
    }

    [TestMethod]
    public void IdentityClassification_DoesNotTreatSameNameOrMissingSourceAsIdentityProof()
    {
        const string firstSource = "jra-horse:2024108210";
        const string secondSource = "jra-horse:2024108288";

        var sameDisplayNameDifferentSource = IdentityMigrationPreflightPolicy.ClassifyIdentityEvidence(
            rootObserved: true, initialCanonicalJraIdentity: firstSource, currentCanonicalJraIdentity: secondSource);
        var matchingDisplayNamesMissingSource = IdentityMigrationPreflightPolicy.ClassifyIdentityEvidence(
            rootObserved: true, initialCanonicalJraIdentity: null, currentCanonicalJraIdentity: null);
        var orphanReference = IdentityMigrationPreflightPolicy.ClassifyIdentityEvidence(
            rootObserved: false, initialCanonicalJraIdentity: firstSource, currentCanonicalJraIdentity: firstSource);
        var malformedIdentity = IdentityMigrationPreflightPolicy.ClassifyIdentityEvidence(
            rootObserved: true, initialCanonicalJraIdentity: "jra-horse:unknown", currentCanonicalJraIdentity: null);

        Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Conflict, sameDisplayNameDifferentSource);
        Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unverifiable, matchingDisplayNamesMissingSource);
        Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unverifiable, orphanReference);
        Assert.AreEqual(IdentityMigrationPreflightObservationStatus.UnknownShape, malformedIdentity);
    }

    [TestMethod]
    public void CanonicalIdentifiers_AcceptOnlyCanonicalHorseAndJraForms()
    {
        Assert.IsTrue(IdentityMigrationPreflightPolicy.IsCanonicalHorseId(
            "horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20"));
        Assert.IsFalse(IdentityMigrationPreflightPolicy.IsCanonicalHorseId(
            "horse-4D2F2A58-0E31-45D6-BF9C-9E6F3B5A7F20"));
        Assert.IsFalse(IdentityMigrationPreflightPolicy.IsCanonicalHorseId("not-an-id"));
        Assert.IsTrue(IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity("jra-horse:2024108210"));
        Assert.IsFalse(IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity("jra-horse:202410821０"));
        Assert.IsFalse(IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity("https://example.invalid/horse"));
    }

    [TestMethod]
    public void ProtectedCursor_RoundTripsBoundedInternalPosition()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var protector = CreateProtector(time);
        var token = protector.Protect(CreateCursor("schema-a"));

        Assert.IsTrue(token.Length <= IdentityMigrationPreflightPolicy.MaximumCursorCharacters);
        Assert.IsTrue(protector.TryUnprotect(token, "schema-a", out var actual));
        Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRaceHistories, actual!.Phase);
        Assert.AreEqual("horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20", actual.Key);
        Assert.AreEqual("schema-a", actual.SchemaAssumptions);
        Assert.AreEqual(time.GetUtcNow().Add(IdentityMigrationPreflightPolicy.CursorLifetime), actual.ExpiresAtUtc);
    }

    [TestMethod]
    public void ProtectedCursor_FailsClosedForTamperingOversizeSchemaMismatchAndExpiredTime()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var protector = CreateProtector(time);
        var token = protector.Protect(CreateCursor("schema-a"));
        var last = token[^1] == 'A' ? 'B' : 'A';
        var tampered = token[..^1] + last;

        Assert.IsFalse(protector.TryUnprotect(tampered, "schema-a", out _));
        Assert.IsFalse(protector.TryUnprotect(new string('x', IdentityMigrationPreflightPolicy.MaximumCursorCharacters + 1), "schema-a", out _));
        Assert.IsFalse(protector.TryUnprotect(token, "schema-b", out _));

        time.Advance(IdentityMigrationPreflightPolicy.CursorLifetime + TimeSpan.FromTicks(1));
        Assert.IsFalse(protector.TryUnprotect(token, "schema-a", out _));
    }

    [TestMethod]
    public void ProtectedCursor_FailsClosedAfterDataProtectionKeyRestart()
    {
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var token = CreateProtector(time).Protect(CreateCursor("schema-a"));
        var restarted = CreateProtector(time);

        Assert.IsFalse(restarted.TryUnprotect(token, "schema-a", out _));
    }

    [TestMethod]
    public void ProtectedCursor_RejectsSerializedPayloadBeyondBound()
    {
        var protector = CreateProtector(new TestTimeProvider(DateTimeOffset.UtcNow));
        var oversizedState = CreateCursor("schema-a") with { Key = new string('\u3042', 512) };

        Assert.Throws<ArgumentException>(() => protector.Protect(oversizedState));
    }

    [TestMethod]
    public void PageJson_ContainsOnlyAllowlistedObservationFieldsAndNoMigrationDecision()
    {
        var evidence = new IdentityMigrationPreflightHorseEvidence(
            "horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20",
            IdentityMigrationPreflightPhase.HorseRoots,
            IdentityMigrationPreflightObservationStatus.Unverifiable,
            "同名馬",
            "同名馬",
            null,
            "同名馬",
            "同名馬",
            null,
            null,
            IdentityMigrationPreflightObservationStatus.Unverifiable,
            [new("202610120101", 4)],
            1,
            false);
        var page = new IdentityMigrationPreflightPage(
            IdentityMigrationPreflightPageState.InProgress,
            IdentityMigrationPreflightPhase.HorseRoots,
            DateTimeOffset.Parse("2026-10-12T03:00:00Z"),
            1,
            new(1, 0, 0, 0, 0, 0, 0, 0, 1, 0),
            [new(IdentityMigrationPreflightSurface.EventStore,
                IdentityMigrationPreflightObservationStatus.Observed,
                DateTimeOffset.Parse("2026-10-12T03:00:00Z"), 5, 0)],
            [evidence],
            "opaque-token");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var evidenceJson = json.RootElement.GetProperty("evidence")[0];
        CollectionAssert.AreEquivalent(
            new[]
            {
                "horseId", "phase", "status", "currentRegisteredName", "currentNormalizedName", "currentBirthDate",
                "initialRegisteredName", "initialNormalizedName", "initialBirthDate", "canonicalJraIdentity",
                "jraIdentityStatus", "relatedRaces", "relatedRaceCount", "relatedRacesTruncated",
            },
            evidenceJson.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.IsFalse(json.RootElement.ToString().Contains("candidate", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.RootElement.ToString().Contains("sourceUrl", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.RootElement.ToString().Contains("metadata", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("Unverifiable", evidenceJson.GetProperty("status").GetString());
    }

    private static IdentityMigrationPreflightCursorProtector CreateProtector(TimeProvider timeProvider) =>
        new(new EphemeralDataProtectionProvider(), timeProvider);

    private static IdentityMigrationPreflightCursor CreateCursor(string schemaAssumptions) => new(
        PolicyVersion: 0,
        StoreSelection: string.Empty,
        SchemaAssumptionVersion: string.Empty,
        SchemaAssumptions: schemaAssumptions,
        Phase: IdentityMigrationPreflightPhase.HorseRaceHistories,
        Key: "horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20",
        ExpiresAtUtc: default);

    private static void AssertExpected<T>(T expected, T actual) => Assert.AreEqual(expected, actual);

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
