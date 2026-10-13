using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationManifestPolicyTests
{
    private const string FirstJraIdentity = "jra-horse:2024100828";
    private const string SecondJraIdentity = "jra-horse:2024108210";

    [TestMethod]
    public void ValidManifestDerivesDestinationThroughSharedGeneratorAndCopiesCallerInput()
    {
        var entries = new List<HorseIdentityMigrationManifestEntry?>
        {
            new HorseIdentityMigrationManifestEntry(Horse("1"), FirstJraIdentity, HypothesisAccepted: true),
        };
        var manifest = new HorseIdentityMigrationManifest(entries);
        entries.Clear();

        var result = HorseIdentityMigrationManifestPolicy.Validate(manifest);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(1, result.Mappings.Count);
        var normalizedSource = JraSourceIdentity.NormalizeHorseUrl(
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024100828/2E");
        Assert.IsNotNull(normalizedSource);
        Assert.AreEqual(
            DeterministicIdGenerator.BuildHorseId("any display name", normalizedSource!.ToString()),
            result.Mappings[0].DestinationHorseId);
        Assert.AreEqual("horse-9ffccf98-2fb3-57d1-9e20-fa0efd9c962c", result.Mappings[0].DestinationHorseId);
        Assert.AreEqual(FirstJraIdentity, result.Mappings[0].JraIdentity);

        var novaUrl = JraSourceIdentity.NormalizeHorseUrl(
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024108210/D8");
        Assert.IsNotNull(novaUrl);
        Assert.IsTrue(JraSourceIdentity.TryNormalizeHorse(novaUrl!.ToString(), out var normalizedNova));
        Assert.AreEqual(SecondJraIdentity, normalizedNova);
        Assert.AreEqual("horse-bd4c07df-8a9a-510e-a754-9dc1123ebb63",
            DeterministicIdGenerator.BuildHorseId("same display name", novaUrl.ToString()));
    }

    [TestMethod]
    public void NullEmptyOversizedAndUnacceptedHypothesisAreRejected()
    {
        AssertError(null, HorseIdentityMigrationManifestErrorCode.Required);
        AssertError(new HorseIdentityMigrationManifest(null), HorseIdentityMigrationManifestErrorCode.Required);
        AssertError(new HorseIdentityMigrationManifest(Array.Empty<HorseIdentityMigrationManifestEntry?>()),
            HorseIdentityMigrationManifestErrorCode.Empty);
        AssertError(Manifest((HorseIdentityMigrationManifestEntry?)null), HorseIdentityMigrationManifestErrorCode.MissingEntry);
        AssertError(new HorseIdentityMigrationManifest(Enumerable.Range(0, 17)
            .Select(index => (HorseIdentityMigrationManifestEntry?)new HorseIdentityMigrationManifestEntry(
                Horse(index.ToString()), Identity(index), true)).ToArray()),
            HorseIdentityMigrationManifestErrorCode.TooManyEntries);
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry(Horse("1"), FirstJraIdentity, HypothesisAccepted: false)),
            HorseIdentityMigrationManifestErrorCode.HypothesisNotAccepted);
    }

    [TestMethod]
    public void SixteenEntriesAreAcceptedAndOversizedCallerIsCopiedOnlyThroughOverflowSentinel()
    {
        var sixteen = Enumerable.Range(1, HorseIdentityMigrationManifest.MaximumEntries)
            .Select(index => (HorseIdentityMigrationManifestEntry?)new HorseIdentityMigrationManifestEntry(
                Horse(index.ToString()), Identity(index), HypothesisAccepted: true)).ToArray();
        Assert.IsTrue(HorseIdentityMigrationManifestPolicy.Validate(new HorseIdentityMigrationManifest(sixteen)).IsValid);

        var oversizedInput = new OversizedEntries(100_000);
        var oversizedManifest = new HorseIdentityMigrationManifest(oversizedInput);
        var oversizedResult = HorseIdentityMigrationManifestPolicy.Validate(oversizedManifest);

        Assert.IsTrue(oversizedManifest.IsOverLimit);
        Assert.AreEqual(HorseIdentityMigrationManifest.MaximumEntries + 1, oversizedManifest.Entries!.Count);
        Assert.AreEqual(HorseIdentityMigrationManifest.MaximumEntries + 1, oversizedInput.IndexReads);
        AssertError(oversizedManifest, HorseIdentityMigrationManifestErrorCode.TooManyEntries);
    }

    [TestMethod]
    public void NonCanonicalIdsAndUnicodeDigitsAreRejectedWithoutNameFallback()
    {
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry("horse-4D2F2A58-0E31-45D6-BF9C-9E6F3B5A7F20", FirstJraIdentity, true)),
            HorseIdentityMigrationManifestErrorCode.InvalidOldHorseId);
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry("legacy-horse-1", FirstJraIdentity, true)),
            HorseIdentityMigrationManifestErrorCode.InvalidOldHorseId);
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry(Horse("1"), "jra-horse:202410821０", true)),
            HorseIdentityMigrationManifestErrorCode.InvalidJraIdentity);
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry(Horse("1"), "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024108210/D8", true)),
            HorseIdentityMigrationManifestErrorCode.InvalidJraIdentity);
    }

    [TestMethod]
    public void DuplicateOldAndConflictingOldIdentityAreRejected()
    {
        var oldHorseId = Horse("1");
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry(oldHorseId, FirstJraIdentity, true),
                new HorseIdentityMigrationManifestEntry(oldHorseId, FirstJraIdentity, true)),
            HorseIdentityMigrationManifestErrorCode.DuplicateOldHorse);
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry(oldHorseId, FirstJraIdentity, true),
                new HorseIdentityMigrationManifestEntry(oldHorseId, SecondJraIdentity, true)),
            HorseIdentityMigrationManifestErrorCode.ConflictingIdentityForOldHorse);
    }

    [TestMethod]
    public void DuplicateDestinationAndMappingCycleAreRejected()
    {
        AssertError(Manifest(new HorseIdentityMigrationManifestEntry(Horse("1"), FirstJraIdentity, true),
                new HorseIdentityMigrationManifestEntry(Horse("2"), FirstJraIdentity, true)),
            HorseIdentityMigrationManifestErrorCode.DuplicateDestination);

        AssertError(Manifest(new HorseIdentityMigrationManifestEntry(Derive(FirstJraIdentity), FirstJraIdentity, true)),
            HorseIdentityMigrationManifestErrorCode.SelfMap);

        var firstDestination = Derive(FirstJraIdentity);
        var secondDestination = Derive(SecondJraIdentity);
        var cycle = Manifest(new HorseIdentityMigrationManifestEntry(secondDestination, FirstJraIdentity, true),
            new HorseIdentityMigrationManifestEntry(firstDestination, SecondJraIdentity, true));

        AssertError(cycle, HorseIdentityMigrationManifestErrorCode.Cycle);
    }

    [TestMethod]
    public void DistinctSourceIdentitiesProduceDistinctDestinationsWithoutDisplayNameInput()
    {
        var result = HorseIdentityMigrationManifestPolicy.Validate(Manifest(
            new HorseIdentityMigrationManifestEntry(Horse("1"), FirstJraIdentity, true),
            new HorseIdentityMigrationManifestEntry(Horse("2"), SecondJraIdentity, true)));

        Assert.IsTrue(result.IsValid);
        Assert.AreNotEqual(result.Mappings[0].DestinationHorseId, result.Mappings[1].DestinationHorseId);
    }

    [TestMethod]
    public void ManifestEntriesAreReadOnlyAfterConstruction()
    {
        var manifest = Manifest(new HorseIdentityMigrationManifestEntry(Horse("1"), FirstJraIdentity, true));
        var list = (IList<HorseIdentityMigrationManifestEntry?>)manifest.Entries!;

        Assert.Throws<NotSupportedException>(() => list[0] = new HorseIdentityMigrationManifestEntry(Horse("2"), SecondJraIdentity, true));
    }

    private static HorseIdentityMigrationManifest Manifest(params HorseIdentityMigrationManifestEntry?[] entries) => new(entries);

    private static void AssertError(HorseIdentityMigrationManifest? manifest, HorseIdentityMigrationManifestErrorCode expected)
    {
        var result = HorseIdentityMigrationManifestPolicy.Validate(manifest);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(error => error.Code == expected),
            $"Expected {expected}; found {string.Join(",", result.Errors.Select(error => error.Code))}.");
        Assert.AreEqual(0, result.Mappings.Count, "An invalid manifest must not expose a partially accepted mapping list.");
    }

    private static string Horse(string suffix) => $"horse-00000000-0000-0000-0000-{suffix.PadLeft(12, '0')}";

    private static string Identity(int index) => $"jra-horse:{index:0000000000}";

    private static string Derive(string identity)
    {
        var sourceUrl = JraSourceIdentity.NormalizeHorseUrl(
            $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00{identity["jra-horse:".Length..]}/manifest");
        Assert.IsNotNull(sourceUrl);
        return DeterministicIdGenerator.BuildHorseId("test", sourceUrl!.ToString());
    }

    private sealed class OversizedEntries(int count) : IReadOnlyList<HorseIdentityMigrationManifestEntry?>
    {
        public int Count { get; } = count;
        public int IndexReads { get; private set; }

        public HorseIdentityMigrationManifestEntry? this[int index]
        {
            get
            {
                IndexReads++;
                return new HorseIdentityMigrationManifestEntry(Horse(index.ToString()), Identity(index), true);
            }
        }

        public IEnumerator<HorseIdentityMigrationManifestEntry?> GetEnumerator() =>
            throw new InvalidOperationException("The bounded snapshot must use only indexed reads.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
