namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class CollectionBatchIdentityConflictException(string code)
    : InvalidOperationException("The batch identifier is already associated with a different or unclassifiable workflow.")
{
    public string Code { get; } = code;
}
