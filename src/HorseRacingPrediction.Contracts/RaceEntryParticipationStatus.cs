namespace HorseRacingPrediction.Contracts;

/// <summary>Participation on the race card, separate from a declared race result.</summary>
public enum RaceEntryParticipationStatus
{
    Active = 0,
    Cancelled = 1,
    Excluded = 2
}
