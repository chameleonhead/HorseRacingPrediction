namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class HorseProfileLegacyLayoutRevisionCondition : INamedRevisionImpactCondition
{
    public string Name => "horse-profile:legacy-layout";

    public bool Matches(RevisionResourceCandidate candidate) =>
        candidate.Resource.Type == ResourceType.Horse
        && candidate.Attributes.TryGetValue("layout", out var layout)
        && string.Equals(layout, "legacy", StringComparison.OrdinalIgnoreCase);
}

public sealed class RaceResultDeadHeatBeforeRevisionFiveCondition : INamedRevisionImpactCondition
{
    public string Name => "race-result:dead-heat-before-rev5";

    public bool Matches(RevisionResourceCandidate candidate) =>
        candidate.Resource.Type == ResourceType.RaceResult
        && candidate.Attributes.TryGetValue("hasDeadHeat", out var value)
        && bool.TryParse(value, out var hasDeadHeat)
        && hasDeadHeat;
}
