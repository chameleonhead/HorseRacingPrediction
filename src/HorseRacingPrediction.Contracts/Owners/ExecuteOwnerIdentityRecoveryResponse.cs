using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Owners;

public sealed record ExecuteOwnerIdentityRecoveryResponse(IReadOnlyList<CollectionRequestReceiptDto> Receipts);
