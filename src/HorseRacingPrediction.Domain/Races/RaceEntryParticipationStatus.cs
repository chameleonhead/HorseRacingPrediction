namespace HorseRacingPrediction.Domain.Races;

/// <summary>Pre-race participation; does not declare a result or cancel the race.</summary>
public enum RaceEntryParticipationStatus
{
    Active = 0,
    Cancelled = 1,
    Excluded = 2
}
