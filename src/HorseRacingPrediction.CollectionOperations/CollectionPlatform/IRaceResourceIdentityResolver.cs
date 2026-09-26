namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

/// <summary>Read-only domain lookup used by both lease and repair-hold boundaries.</summary>
public interface IRaceResourceIdentityResolver
{
    string? Resolve(string resourceId, IReadOnlyDictionary<string, string> attributes);
}
