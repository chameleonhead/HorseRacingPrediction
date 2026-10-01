namespace HorseRacingPrediction.Contracts.Collection;

public sealed record PreviewRacePeriodRecollectionInputDto(DateOnly From, DateOnly To,
    string Provider = "JRA", string? BatchId = null);
