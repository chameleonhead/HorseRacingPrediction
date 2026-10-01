namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateRecollectionBatchInputDto(string Mode, string? Definition = null, int? Revision = null,
    string? Provider = null, DateOnly? From = null, DateOnly? To = null, string? BatchId = null,
    CollectionLane? Lane = null, int? Priority = null);
