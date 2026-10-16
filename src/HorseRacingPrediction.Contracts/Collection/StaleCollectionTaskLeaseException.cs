namespace HorseRacingPrediction.Contracts.Collection;

public sealed class StaleCollectionTaskLeaseException()
    : Exception("The collection task lease is no longer valid.")
{
}
