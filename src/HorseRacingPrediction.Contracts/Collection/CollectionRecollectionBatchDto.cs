namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRecollectionBatchDto(string Mode,
    RevisionRecollectionExpansionDto? Revision, RacePeriodRecollectionReceiptDto? RacePeriod);
