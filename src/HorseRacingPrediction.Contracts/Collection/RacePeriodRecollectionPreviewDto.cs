namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RacePeriodRecollectionPreviewDto(DateOnly From, DateOnly To, int InclusiveDays,
    string Provider);
