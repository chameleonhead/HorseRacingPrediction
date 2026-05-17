namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class RaceDataCollectionStatusEntity
{
    public string RaceKey { get; set; } = string.Empty;
    public DateOnly RaceDate { get; set; }
    public string Racecourse { get; set; } = string.Empty;
    public int RaceNumber { get; set; }
    public string? RaceId { get; set; }
    public string? RaceName { get; set; }
    public string? RaceCardUrl { get; set; }
    public RaceDataCollectionState RaceCardStatus { get; set; }
    public RaceDataCollectionErrorCode? RaceCardErrorCode { get; set; }
    public string? RaceCardErrorReason { get; set; }
    public DateTimeOffset? RaceCardUpdatedAt { get; set; }
    public string? RaceResultUrl { get; set; }
    public RaceDataCollectionState RaceResultStatus { get; set; }
    public RaceResultAcquisitionOrigin? RaceResultOrigin { get; set; }
    public string? RequestedByRaceId { get; set; }
    public RaceDataCollectionErrorCode? RaceResultErrorCode { get; set; }
    public string? RaceResultErrorReason { get; set; }
    public DateTimeOffset? RaceResultUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}