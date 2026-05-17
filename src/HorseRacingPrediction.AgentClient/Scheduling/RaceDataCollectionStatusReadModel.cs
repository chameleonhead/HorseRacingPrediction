namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record RaceDataCollectionStatusReadModel(
    DateOnly RaceDate,
    string Racecourse,
    int RaceNumber,
    string? RaceId,
    string? RaceName,
    string? RaceCardUrl,
    RaceDataCollectionState RaceCardStatus,
    RaceDataCollectionErrorCode? RaceCardErrorCode,
    string? RaceCardErrorReason,
    DateTimeOffset? RaceCardUpdatedAt,
    string? RaceResultUrl,
    RaceDataCollectionState RaceResultStatus,
    RaceResultAcquisitionOrigin? RaceResultOrigin,
    string? RequestedByRaceId,
    RaceDataCollectionErrorCode? RaceResultErrorCode,
    string? RaceResultErrorReason,
    DateTimeOffset? RaceResultUpdatedAt,
    DateTimeOffset UpdatedAt);