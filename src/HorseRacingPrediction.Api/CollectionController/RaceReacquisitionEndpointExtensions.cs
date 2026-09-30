namespace HorseRacingPrediction.Api.CollectionController;

// Kept temporarily as the shared JRA course normalizer used by domain-write validation and
// the existing UI. The legacy reacquisition endpoints themselves have been removed.
public static class RaceReacquisitionEndpointExtensions
{
    internal static string? ResolveCourse(string? value) => HorseRacingPrediction.Contracts.Races.RaceCourseIdentity.Canonicalize(value);
}
